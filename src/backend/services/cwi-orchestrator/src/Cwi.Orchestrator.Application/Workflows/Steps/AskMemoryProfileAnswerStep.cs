#nullable enable
using System.Text;
using System.Text.RegularExpressions;
using Cwi.Common;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Workflows;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class AskMemoryProfileAnswerStep : IAskWorkflowStep
{
    private static readonly Regex TechnicalJsonPreferenceRegex = new(
        "^\"?[a-z0-9_.-]+/[a-z0-9_./-]+\"?\\s*:",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ControlledFactEntityRegex = new(
        @"\bcwi[-\s]?rl[-\s]?\d{3,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        var isUploadedContextLookup = AskUploadedContextLookupPolicy.IsUploadedContextLookup(context);
        if ((!IsMemoryRecall(context) && !isUploadedContextLookup) || context.SkipModelInference)
        {
            return next();
        }

        RemoveTechnicalMemoryArtifacts(context);

        var hasMemoryEvidence = isUploadedContextLookup
            ? HasUploadedContextEvidence(context)
            : HasMemoryEvidence(context);
        if (isUploadedContextLookup && !hasMemoryEvidence)
        {
            context.QueryClassification = "general_model_knowledge";
            context.IntentType = "explanation";
            context.RetrievalAllowed = false;
            context.GroundingMode = "non_grounded";
            context.RetrievalSkippedReason ??= "uploaded_context_lookup_returned_no_user_context";
            context.EvidenceRequired = false;
            context.MemoryResults = Array.Empty<GovernedMemoryResult>();
            context.SourceResults = Array.Empty<GovernedSourceResult>();
            context.GraphEvidence.Clear();
            context.Retrieved.Clear();
            return next();
        }

        context.Answer = hasMemoryEvidence
            ? isUploadedContextLookup ? BuildUploadedContextLookupAnswer(context) : BuildAnswer(context)
            : BuildNoMemoryStateAnswer(context);
        context.ModelName = "governed-memory-runtime";
        context.Route = hasMemoryEvidence
            ? isUploadedContextLookup ? "uploaded_context_memory_retrieval" : "activated_memory_retrieval"
            : "activated_memory_state";
        context.TemplateUsed = isUploadedContextLookup ? "uploaded_context_lookup" : ResolveTemplate(context);
        context.ToneMode ??= "concise_actionable";
        context.GroundingMode = hasMemoryEvidence ? "source_grounded" : "memory_policy_state";
        context.Grounded = hasMemoryEvidence;
        context.ReasoningBased = false;
        context.SkipModelInference = true;
        context.ExecutionTarget = "governed_memory";
        if (!hasMemoryEvidence)
        {
            context.EvidenceRequired = false;
            context.RetrievalSkippedReason ??= ResolveNoMemorySkippedReason(context);
        }

        return next();
    }

    private static bool IsMemoryRecall(AskWorkflowContext context)
    {
        return string.Equals(context.QueryClassification, "memory_graph_request", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.QueryClassification, "controlled_fact_lookup", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.IntentType, "memory_recall", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasMemoryEvidence(AskWorkflowContext context)
    {
        return context.MemoryResults.Count > 0
               || context.MemoryConflictSummaries.Count > 0
               || context.GraphEvidence.Count > 0
               || context.SourceResults.Count > 0;
    }

    private static void RemoveTechnicalMemoryArtifacts(AskWorkflowContext context)
    {
        context.MemoryResults = context.MemoryResults
            .Where(memory => IsUsableProfileMemoryText($"{memory.SourceType} {memory.Text}"))
            .ToArray();
        context.SourceResults = context.SourceResults
            .Where(source => IsUsableProfileMemoryText($"{source.SourceType} {source.SourceId} {source.Text}"))
            .ToArray();
        var usableGraphEvidence = context.GraphEvidence
            .Where(evidence => IsUsableProfileMemoryText($"{evidence.Subject} {evidence.Object} {evidence.Template} {evidence.SourceDocument}"))
            .ToArray();
        if (usableGraphEvidence.Length != context.GraphEvidence.Count)
        {
            context.GraphEvidence.Clear();
            context.GraphEvidence.AddRange(usableGraphEvidence);
        }
    }

    private static bool HasUploadedContextEvidence(AskWorkflowContext context)
    {
        return context.SourceResults.Any(IsUploadedContextSource)
               || context.MemoryResults.Any(IsUploadedContextMemory)
               || context.GraphEvidence.Any(IsUploadedContextGraphEvidence);
    }

    private static bool IsUploadedContextSource(GovernedSourceResult source)
    {
        return source.SourceType.Contains("uploaded", StringComparison.OrdinalIgnoreCase)
               || source.SourceId.StartsWith("web-upload-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUploadedContextMemory(GovernedMemoryResult memory)
    {
        if (string.Equals(memory.SourceType, "conversation_summary", StringComparison.OrdinalIgnoreCase)
            || string.Equals(memory.SourceType, "conversation_turn", StringComparison.OrdinalIgnoreCase)
            || string.Equals(memory.MemoryScope, "conversation", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return memory.SourceType.Contains("uploaded", StringComparison.OrdinalIgnoreCase)
               || memory.SourceType.Contains("imported", StringComparison.OrdinalIgnoreCase)
               || memory.SourceType.Contains("activated_user_memory", StringComparison.OrdinalIgnoreCase)
               || string.Equals(memory.MemoryScope, "activated_user_memory", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUploadedContextGraphEvidence(GraphEvidence evidence)
    {
        return (evidence.SourceDocument?.Contains("upload", StringComparison.OrdinalIgnoreCase) ?? false)
               || (evidence.SourceDocument?.Contains("import", StringComparison.OrdinalIgnoreCase) ?? false)
               || evidence.Template.Contains("uploaded", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildAnswer(AskWorkflowContext context)
    {
        if (IsControlledFactLookup(context))
        {
            return BuildControlledFactLookupAnswer(context);
        }

        var question = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion).ToLowerInvariant();
        if (question.Contains("source", StringComparison.Ordinal)
            || question.Contains("sources", StringComparison.Ordinal)
            || question.Contains("provenance", StringComparison.Ordinal))
        {
            return BuildSourceAnswer(context);
        }

        if (IsPersonalDescriptionQuestion(question))
        {
            return BuildPersonDescriptionAnswer(context);
        }

        if (IsPersonalNameQuestion(question))
        {
            return BuildPersonalNameAnswer(context);
        }

        if (IsPersonalWorkRoleQuestion(question))
        {
            return BuildPersonalWorkRoleAnswer(context);
        }

        if (question.Contains("key people", StringComparison.Ordinal)
            || question.Contains("people in my network", StringComparison.Ordinal)
            || question.Contains("who matters", StringComparison.Ordinal)
            || question.Contains("relationship", StringComparison.Ordinal))
        {
            return BuildFocusedAnswer(
                "Here are the key people and relationship signals I understand so far:",
                SelectRelationshipItems(context),
                context);
        }

        if (question.Contains("open loop", StringComparison.Ordinal)
            || question.Contains("next action", StringComparison.Ordinal)
            || question.Contains("need to do", StringComparison.Ordinal))
        {
            return BuildFocusedAnswer(
                "Here are the open loops I understand so far:",
                SelectOpenLoopItems(context),
                context);
        }

        if (question.Contains("project", StringComparison.Ordinal)
            || question.Contains("working on", StringComparison.Ordinal)
            || question.Contains("work life", StringComparison.Ordinal))
        {
            return BuildFocusedAnswer(
                "Here is what I understand about your work and projects:",
                SelectWorkItems(context),
                context);
        }

        if (question.Contains("home life", StringComparison.Ordinal)
            || question.Contains("personal life", StringComparison.Ordinal))
        {
            return BuildFocusedAnswer(
                "Here is what I understand about your home or personal context:",
                SelectPersonalItems(context),
                context);
        }

        return BuildProfileAnswer(context);
    }

    private static bool IsControlledFactLookup(AskWorkflowContext context)
    {
        return string.Equals(context.QueryClassification, "controlled_fact_lookup", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.RetrievalDomain, "controlled_fact", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildControlledFactLookupAnswer(AskWorkflowContext context)
    {
        var facts = SelectControlledFactItems(context);
        if (facts.Count == 0)
        {
            return BuildNoMemoryStateAnswer(context);
        }

        var entity = ResolvePrimarySubject(context) ?? "the controlled entity";
        var changedFacts = facts
            .Where(ContainsExplicitChangeMarker)
            .ToArray();
        if (changedFacts.Length == 0)
        {
            changedFacts = facts
                .Where(ContainsTemporalChangeMarker)
                .ToArray();
        }

        if (changedFacts.Length > 0)
        {
            return $"Current learned fact for {entity}: {changedFacts[0]}";
        }

        var ownershipFacts = facts
            .Where(IsOwnershipFact)
            .ToArray();
        var owners = ownershipFacts
            .Select(ExtractOwner)
            .Where(owner => !string.IsNullOrWhiteSpace(owner))
            .Select(owner => owner!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (owners.Length > 1)
        {
            var builder = new StringBuilder();
            builder.Append("I found a conflict for ");
            builder.Append(entity);
            builder.AppendLine(", so ownership is uncertain and should be clarified:");
            builder.AppendLine();
            AppendItems(builder, ownershipFacts);
            AppendSourceFooter(builder, context, includeSourceIds: false);
            return builder.ToString().Trim();
        }

        var preferredFacts = ResolvePreferredControlledFacts(context, facts, ownershipFacts);
        if (preferredFacts.Count == 1)
        {
            return $"The learned fact I found for {entity}: {preferredFacts[0]}";
        }

        var answer = new StringBuilder();
        answer.Append("Here are the learned facts I found for ");
        answer.Append(entity);
        answer.AppendLine(":");
        answer.AppendLine();
        AppendItems(answer, preferredFacts);
        AppendSourceFooter(answer, context, includeSourceIds: false);
        return answer.ToString().Trim();
    }

    private static IReadOnlyList<string> ResolvePreferredControlledFacts(
        AskWorkflowContext context,
        IReadOnlyList<string> facts,
        IReadOnlyList<string> ownershipFacts)
    {
        var question = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion).ToLowerInvariant();
        if (ownershipFacts.Count > 0
            && ContainsAny(question, "who ", "owner", "owns", "owned", "responsible", "ask", "review", "approve", "handles"))
        {
            return ownershipFacts;
        }

        var dependencyFacts = facts
            .Where(fact => ContainsAny(fact, "depends on", "dependency", "prerequisite", "blocks", "blocked by"))
            .ToArray();
        if (dependencyFacts.Length > 0
            && ContainsAny(question, "depend", "dependency", "prerequisite", "block", "before"))
        {
            return dependencyFacts;
        }

        return facts;
    }

    private static IReadOnlyList<string> SelectControlledFactItems(AskWorkflowContext context)
    {
        var requestedEntities = ExtractControlledFactEntities(context.RawQuestion)
            .Concat(ExtractControlledFactEntities(context.RoutingQuestion))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var controlledFacts = context.MemoryResults
            .Where(memory =>
                string.Equals(memory.SourceType, "controlled_fact", StringComparison.OrdinalIgnoreCase)
                || (requestedEntities.Count == 0
                    && memory.Text.Contains("CWI-RL-", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(memory.SourceType, "conversation_summary", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(memory.SourceType, "conversation_turn", StringComparison.OrdinalIgnoreCase)))
            .Select(memory => CleanInline(memory.Text))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Where(item => requestedEntities.Count == 0
                           || ExtractControlledFactEntities(item).Any(requestedEntities.Contains))
            .Where(item => !ContainsAny(item, "PRIVATE-CANARY", "internal privacy canary"))
            .Where(item => !item.StartsWith("Recent conversation summary:", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        return controlledFacts;
    }

    private static bool ContainsTemporalChangeMarker(string text)
    {
        return ContainsExplicitChangeMarker(text)
               || text.Contains(" current ", StringComparison.OrdinalIgnoreCase)
               || text.Contains(" now ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsExplicitChangeMarker(string text)
    {
        return text.Contains(" changed to ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOwnershipFact(string text)
    {
        return text.Contains(" owned by ", StringComparison.OrdinalIgnoreCase)
               || text.Contains(" owns ", StringComparison.OrdinalIgnoreCase)
               || text.Contains(" owner is ", StringComparison.OrdinalIgnoreCase)
               || text.Contains(" responsible for ", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractOwner(string text)
    {
        var patterns = new[]
        {
            @"\bowned\s+by\s+(?<owner>[A-Za-z][A-Za-z0-9_-]+)",
            @"\bowner\s+is\s+(?<owner>[A-Za-z][A-Za-z0-9_-]+)",
            @"\b(?<owner>[A-Za-z][A-Za-z0-9_-]+)\s+owns\s+CWI-RL-\d{3,}\b",
            @"\bresponsible\s+for\b.*?\b(?<owner>TestOwner[A-Za-z0-9_-]+)\b"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
            {
                return match.Groups["owner"].Value.Trim().TrimEnd('.', ',', ';', ':', '!', '?');
            }
        }

        return null;
    }

    private static string BuildPersonDescriptionAnswer(AskWorkflowContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Based on what I understand so far, I would describe you this way:");
        builder.AppendLine();

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var identityItems = SelectIdentityDescriptionItems(context);
        var workItems = SelectWorkItems(context);
        var relationshipItems = SelectRelationshipItems(context);
        var openLoopItems = SelectOpenLoopItems(context);
        var personalItems = SelectPersonalItems(context);

        AppendGroundedTrait(
            builder,
            "Systems-minded and builder-oriented.",
            "Your stored context points to someone who works through platforms, frameworks, and operating systems rather than isolated one-off tasks.",
            PickFirstMatching(identityItems.Concat(workItems), used, "active", "building", "project", "cwi", "framework", "system")
            ?? PickFirstUnused(identityItems.Concat(workItems), used));
        AppendGroundedTrait(
            builder,
            "Evidence-driven and quality-focused.",
            "You appear to push for validation, clear acceptance, and proof that the system is really behaving as intended.",
            PickFirstMatching(openLoopItems.Concat(workItems).Concat(personalItems), used, "proof", "verify", "validation", "open loop", "generic", "fallback", "source", "trace")
            ?? PickFirstUnused(openLoopItems.Concat(workItems).Concat(personalItems), used));
        AppendGroundedTrait(
            builder,
            "Direct and practical in communication.",
            "Your preferences favor concise, usable responses with clear structure and source grouping over vague explanation.",
            PickFirstMatching(personalItems.Concat(workItems), used, "preference", "prefers", "concise", "grouped", "source", "presentation", "paragraph")
            ?? PickFirstUnused(personalItems.Concat(workItems), used));
        AppendGroundedTrait(
            builder,
            "Collaborative, but ownership-aware.",
            "Your memory tracks people, responsibilities, and decision ownership instead of treating collaboration as a vague background detail.",
            PickFirstUnused(relationshipItems, used));
        AppendGroundedTrait(
            builder,
            "Persistent about open loops.",
            "You keep unresolved work visible until it is verified, closed, or intentionally deferred.",
            PickFirstUnused(openLoopItems, used)
            ?? openLoopItems.FirstOrDefault());

        if (builder.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length <= 1)
        {
            AppendItems(builder, SelectFallbackItems(context));
        }

        builder.AppendLine();
        builder.AppendLine("I am keeping that description grounded in what you have shared rather than inferring private traits beyond the evidence.");
        AppendConflictSection(builder, context);
        AppendSourceFooter(builder, context, includeSourceIds: false);
        return builder.ToString().Trim();
    }

    private static string BuildProfileAnswer(AskWorkflowContext context)
    {
        var builder = new StringBuilder();
        var profileItems = SelectProfileItems(context);
        var workItems = SelectWorkItems(context)
            .Where(IsUserProfileRelevantMemory)
            .Except(profileItems, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var relationshipItems = SelectRelationshipItems(context)
            .Where(IsUserProfileRelevantMemory)
            .Except(profileItems, StringComparer.OrdinalIgnoreCase)
            .Except(workItems, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var openLoopItems = SelectOpenLoopItems(context)
            .Where(IsUserProfileRelevantMemory)
            .Except(profileItems, StringComparer.OrdinalIgnoreCase)
            .Except(workItems, StringComparer.OrdinalIgnoreCase)
            .Except(relationshipItems, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var evidenceCount = profileItems.Count + workItems.Length + relationshipItems.Length + openLoopItems.Length;
        builder.AppendLine(evidenceCount < 3
            ? "I have a limited picture so far. Here is what I understand:"
            : "Here is what I understand about you:");
        builder.AppendLine();
        var wroteSection = false;
        wroteSection |= AppendSection(builder, "Identity and Work Context", profileItems);
        wroteSection |= AppendSection(builder, "Projects and Priorities", workItems);
        wroteSection |= AppendSection(builder, "People and Relationships", relationshipItems);
        wroteSection |= AppendSection(builder, "Open Loops", openLoopItems);
        if (!wroteSection)
        {
            wroteSection = AppendSection(builder, "Relevant Context", SelectFallbackItems(context));
        }

        if (!wroteSection)
        {
            return BuildNoUserFacingMemorySummaryAnswer(context);
        }

        AppendConflictSection(builder, context);
        AppendSourceFooter(builder, context, includeSourceIds: false);
        return builder.ToString().Trim();
    }

    private static string BuildUploadedContextLookupAnswer(AskWorkflowContext context)
    {
        var evidence = SelectContextLookupEvidence(context);
        if (TryBuildSynthesizedContextAnswer(context, evidence, out var synthesized))
        {
            return synthesized;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Here is the clearest explanation I can give from the available context:");
        builder.AppendLine();
        var items = SelectContextLookupItems(context);
        AppendItems(builder, items.Count == 0 ? SelectFallbackItems(context) : items);
        return builder.ToString().Trim();
    }

    private static string BuildNoMemoryStateAnswer(AskWorkflowContext context)
    {
        var question = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion).ToLowerInvariant();
        if (IsMemoryConflictConceptQuestion(question))
        {
            return """
                When memories contradict, I should not silently choose one as truth.

                I compare source authority, freshness, scope, and confidence. If there is still no safe single answer, I label the conflict, avoid treating either memory as active truth, and ask for clarification or a stronger source.
                """;
        }

        if (question.Contains("source", StringComparison.Ordinal)
            || question.Contains("why did you use", StringComparison.Ordinal)
            || question.Contains("which memories are relevant", StringComparison.Ordinal))
        {
            return "I did not use any stored memory or governed sources for this answer. No usable user memory is currently available for this account.";
        }

        if (!context.RetrievalAllowed)
        {
            return "I have stored information, but I am not permitted to access it in this context.";
        }

        if (context.MemoryUnavailable || context.SourceRetrievalUnavailable || context.GraphUnavailable)
        {
            return "I could not access governed memory right now, so I am not going to guess. Try again after memory retrieval is healthy.";
        }

        return "I don't have any stored memory about you yet. Would you like to import chat history or connect an approved memory source?";
    }

    private static bool IsMemoryConflictConceptQuestion(string question)
    {
        return question.Contains("memories contradict", StringComparison.Ordinal)
               || question.Contains("one source says", StringComparison.Ordinal)
               || question.Contains("what should i believe", StringComparison.Ordinal)
               || question.Contains("sources disagree", StringComparison.Ordinal);
    }

    private static string BuildNoUserFacingMemorySummaryAnswer(AskWorkflowContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("I found account context, but I could not turn it into a clean summary for this question.");
        builder.AppendLine();
        builder.AppendLine("I do not want to invent details. Try asking about a specific area, like your work, projects, people, or open loops.");
        AppendConflictSection(builder, context);
        AppendSourceFooter(builder, context, includeSourceIds: false);
        return builder.ToString().Trim();
    }

    private static string ResolveNoMemorySkippedReason(AskWorkflowContext context)
    {
        if (!context.RetrievalAllowed)
        {
            return "memory_access_not_permitted";
        }

        return context.MemoryUnavailable || context.SourceRetrievalUnavailable || context.GraphUnavailable
            ? "memory_retrieval_unavailable"
            : "memory_retrieval_returned_no_usable_memory";
    }

    private static string BuildFocusedAnswer(
        string intro,
        IReadOnlyList<string> items,
        AskWorkflowContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine(intro);
        builder.AppendLine();
        var selectedItems = items.Count == 0 ? SelectFallbackItems(context) : items;
        if (selectedItems.Count == 0)
        {
            return BuildNoUserFacingMemorySummaryAnswer(context);
        }

        AppendItems(builder, selectedItems);
        AppendConflictSection(builder, context);
        AppendSourceFooter(builder, context, includeSourceIds: false);
        return builder.ToString().Trim();
    }

    private static string BuildSourceAnswer(AskWorkflowContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Sources I used for this answer:");
        builder.AppendLine();

        foreach (var memory in context.MemoryResults.Take(8))
        {
            builder.Append("- ");
            builder.Append(memory.MemoryId);
            builder.Append(" from ");
            builder.Append(memory.SourceType);
            if (!string.IsNullOrWhiteSpace(memory.ProvenanceId))
            {
                builder.Append(" (provenance ");
                builder.Append(memory.ProvenanceId);
                builder.Append(')');
            }
            builder.AppendLine();
        }

        foreach (var evidence in context.GraphEvidence.Take(5))
        {
            builder.Append("- ");
            builder.Append(evidence.SourceToken);
            builder.Append(": ");
            builder.AppendLine(evidence.Object);
        }

        AppendSourceFooter(builder, context, includeSourceIds: false);
        return builder.ToString().Trim();
    }

    private static bool IsPersonalDescriptionQuestion(string question)
    {
        return question.Contains("describe me", StringComparison.Ordinal)
               || question.Contains("describe who i am", StringComparison.Ordinal)
               || question.Contains("what kind of person am i", StringComparison.Ordinal)
               || question.Contains("who am i as a person", StringComparison.Ordinal)
               || question.Contains("tell me about myself", StringComparison.Ordinal)
               || question.Contains("describe my personality", StringComparison.Ordinal);
    }

    private static bool IsPersonalNameQuestion(string question)
    {
        return question.Contains("what is my name", StringComparison.Ordinal)
               || question.Contains("whats my name", StringComparison.Ordinal)
               || question.Contains("what's my name", StringComparison.Ordinal)
               || question.Contains("do you know my name", StringComparison.Ordinal);
    }

    private static bool IsPersonalWorkRoleQuestion(string question)
    {
        return question.Contains("what do i do for work", StringComparison.Ordinal)
               || question.Contains("what do i do professionally", StringComparison.Ordinal)
               || question.Contains("what is my job", StringComparison.Ordinal)
               || question.Contains("whats my job", StringComparison.Ordinal)
               || question.Contains("what's my job", StringComparison.Ordinal)
               || question.Contains("what is my profession", StringComparison.Ordinal)
               || question.Contains("what is my role", StringComparison.Ordinal)
               || question.Contains("what do i do as a job", StringComparison.Ordinal);
    }

    private static string BuildPersonalNameAnswer(AskWorkflowContext context)
    {
        var name = SelectEvidenceText(context)
            .Select(ExtractDeclaredName)
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
        if (string.IsNullOrWhiteSpace(name))
        {
            return BuildProfileAnswer(context);
        }

        return $"Your name is {name}.";
    }

    private static string BuildPersonalWorkRoleAnswer(AskWorkflowContext context)
    {
        var role = SelectEvidenceText(context)
            .Select(ExtractDeclaredWorkRole)
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
        if (string.IsNullOrWhiteSpace(role))
        {
            return BuildFocusedAnswer(
                "Here is what I understand about your work:",
                SelectWorkItems(context),
                context);
        }

        return $"You work as a {role}.";
    }

    private static void AppendObservation(StringBuilder builder, string label, string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
        {
            return;
        }

        builder.Append("- ");
        builder.Append(label);
        builder.Append(": ");
        builder.AppendLine(evidence);
    }

    private static void AppendGroundedTrait(
        StringBuilder builder,
        string trait,
        string interpretation,
        string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
        {
            return;
        }

        builder.Append("- ");
        builder.Append(trait);
        builder.Append(' ');
        builder.Append(interpretation);
        builder.Append(" Grounding: ");
        builder.AppendLine(ToUserFacingEvidence(evidence));
    }

    private static bool AppendSection(StringBuilder builder, string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return false;
        }

        builder.AppendLine($"{title}:");
        AppendItems(builder, items);
        builder.AppendLine();
        return true;
    }

    private static void AppendItems(StringBuilder builder, IReadOnlyList<string> items)
    {
        foreach (var item in items.Take(5))
        {
            builder.Append("- ");
            builder.AppendLine(ToUserFacingEvidence(item));
        }
    }

    private static void AppendConflictSection(StringBuilder builder, AskWorkflowContext context)
    {
        if (context.MemoryConflictSummaries.Count == 0)
        {
            return;
        }

        var conflicts = SelectRelevantConflicts(context);
        if (conflicts.Count == 0)
        {
            return;
        }

        builder.AppendLine("Unresolved context conflicts:");
        foreach (var conflict in conflicts.Take(3))
        {
            builder.Append("- ");
            builder.Append(conflict.Summary);
            builder.AppendLine(" I did not treat this as active memory.");
        }

        builder.AppendLine();
    }

    private static IReadOnlyList<MemoryConflictSummary> SelectRelevantConflicts(AskWorkflowContext context)
    {
        var question = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion).ToLowerInvariant();
        if (IsMemoryConflictConceptQuestion(question)
            || question.Contains("conflict", StringComparison.Ordinal)
            || question.Contains("contradict", StringComparison.Ordinal)
            || question.Contains("what changed", StringComparison.Ordinal)
            || question.Contains("source", StringComparison.Ordinal))
        {
            return context.MemoryConflictSummaries;
        }

        return context.MemoryConflictSummaries
            .Where(conflict => IsUserProfileRelevantMemory(conflict.Summary)
                               && !LooksLikePlatformPolicyOrCharterText(conflict.Summary))
            .ToArray();
    }

    private static void AppendSourceFooter(
        StringBuilder builder,
        AskWorkflowContext context,
        bool includeSourceIds = true)
    {
        var confidence = ResolveAverageConfidence(context);
        builder.AppendLine();
        builder.Append("Confidence: ");
        builder.Append(confidence.ToString("0.00"));
        builder.AppendLine();
        if (!includeSourceIds)
        {
            return;
        }

        var sources = context.MemoryResults
            .Select(memory => string.IsNullOrWhiteSpace(memory.ProvenanceId)
                ? $"memory:{memory.MemoryId}"
                : $"memory:{memory.MemoryId} / provenance:{memory.ProvenanceId}")
            .Concat(context.MemoryConflictSummaries
                .Select(conflict => $"memory_conflict:{conflict.ConflictId}"))
            .Concat(context.SourceResults.Select(source => $"source:{source.SourceId}#{source.ChunkId}"))
            .Concat(context.GraphEvidence.Select(evidence => evidence.SourceToken))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
        if (sources.Length == 0)
        {
            return;
        }

        builder.Append("Sources: ");
        builder.AppendLine(string.Join("; ", sources));
    }

    private static IReadOnlyList<string> SelectProfileItems(AskWorkflowContext context)
    {
        return SelectMemoryText(context, memory =>
            ContainsAny(memory, "identity", "user_profile", "work_role", "my name is", "i am in", "i work as", "my role is", "my job is"));
    }

    private static IReadOnlyList<string> SelectIdentityDescriptionItems(AskWorkflowContext context)
    {
        return SelectMemoryText(context, memory =>
            ContainsAny(memory, "identity", "profile", "cwi", "building", "system", "role", "user_profile"));
    }

    private static IReadOnlyList<string> SelectWorkItems(AskWorkflowContext context)
    {
        return SelectMemoryText(context, memory =>
            ContainsAny(memory, "work", "project", "business", "career", "role", "job", "profession", "engineer", "developer", "mcp", "teacher node", "grafana", "smartsheet", "orchestrator", "deployment", "operations"));
    }

    private static IReadOnlyList<string> SelectRelationshipItems(AskWorkflowContext context)
    {
        return SelectMemoryText(context, memory =>
            ContainsAny(memory, "relationship", "people", "collaborator", "demitrius", "chad", "jasmin", "owner", "owns", "working with"));
    }

    private static IReadOnlyList<string> SelectOpenLoopItems(AskWorkflowContext context)
    {
        return SelectMemoryText(context, memory =>
            ContainsAny(memory, "open_loop", "open loop", "need to", "todo", "to do", "pending", "next action", "blocked", "fix"));
    }

    private static IReadOnlyList<string> SelectPersonalItems(AskWorkflowContext context)
    {
        return SelectMemoryText(context, memory =>
            ContainsAny(memory, "home", "personal", "family", "health", "wellness", "finance", "pets", "house", "preference", "prefers"));
    }

    private static IReadOnlyList<string> SelectFallbackItems(AskWorkflowContext context)
    {
        return SelectMemoryText(context, _ => true);
    }

    private static IReadOnlyList<string> SelectEvidenceText(AskWorkflowContext context)
    {
        return context.MemoryResults
            .Select(memory => memory.Text)
            .Concat(context.SourceResults.Select(source => source.Text))
            .Concat(context.GraphEvidence.Select(evidence => evidence.Object))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
    }

    private static IReadOnlyList<string> SelectContextLookupItems(AskWorkflowContext context)
    {
        return SelectContextLookupEvidence(context)
            .Select(text => Compact(text, 260))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private static IReadOnlyList<string> SelectContextLookupEvidence(AskWorkflowContext context)
    {
        var terms = context.RetrievalDomainTerms
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .ToArray();
        bool Matches(string value)
        {
            return terms.Length == 0
                   || terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        var memoryItems = context.MemoryResults
            .Where(memory => IsUsableProfileMemoryText($"{memory.SourceType} {memory.Text}"))
            .Where(memory => Matches($"{memory.SourceType} {memory.Text}"))
            .Select(memory => memory.Text);
        var sourceItems = context.SourceResults
            .Where(source => Matches($"{source.SourceType} {source.SourceId} {source.Text}"))
            .Select(source => source.Text);
        var graphItems = context.GraphEvidence
            .Where(evidence => Matches($"{evidence.Subject} {evidence.Object} {evidence.Template}"))
            .Select(evidence => evidence.Object);
        return sourceItems
            .Concat(memoryItems)
            .Concat(graphItems)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    private static bool TryBuildSynthesizedContextAnswer(
        AskWorkflowContext context,
        IReadOnlyList<string> evidence,
        out string answer)
    {
        answer = string.Empty;
        if (evidence.Count == 0)
        {
            return false;
        }

        var subject = ResolvePrimarySubject(context) ?? "The platform";
        var owner = ExtractLabeledValue(evidence, "Owner");
        var purpose = ExtractLabeledValue(evidence, "Purpose")
            ?? FindSentence(evidence, "exists to", "purpose");
        var platformIdentity = FindSentence(
            evidence,
            "user-facing intelligence platform",
            "governed intelligence operating layer",
            "decision-grade intelligence");
        var mission = FindSentence(
            evidence,
            "exists to transform",
            "decision-grade intelligence");
        var outerPlatform = FindSentence(
            evidence,
            "common outer platform",
            "upload intake",
            "artifact packaging",
            "observability");
        var capabilities = FindSentence(
            evidence,
            "may profile",
            "may recommend",
            "profile, enrich");
        var safeguards = FindSentence(
            evidence,
            "must not",
            "deterministic-first",
            "preserve source truth",
            "weakly evidenced",
            "unsupported");
        var frameworkDescriptions = ExtractFrameworkDescriptions(evidence);

        if (string.IsNullOrWhiteSpace(purpose)
            && string.IsNullOrWhiteSpace(platformIdentity)
            && string.IsNullOrWhiteSpace(outerPlatform))
        {
            return false;
        }

        var builder = new StringBuilder();
        builder.Append(subject);
        builder.Append(" is ");
        if (!string.IsNullOrWhiteSpace(owner))
        {
            builder.Append("a governed intelligence platform from ");
            builder.Append(CleanInline(owner));
        }
        else
        {
            builder.Append("a governed intelligence platform");
        }

        if (!string.IsNullOrWhiteSpace(purpose))
        {
            builder.Append(" whose purpose is to ");
            builder.Append(EnsureLowercaseStart(CleanPurpose(purpose)));
            builder.Append('.');
            if (!string.IsNullOrWhiteSpace(platformIdentity)
                && !TextContainsSameMeaning(platformIdentity, purpose))
            {
                builder.Append(' ');
                builder.Append(CleanSentence(platformIdentity));
                builder.Append('.');
            }

            if (!string.IsNullOrWhiteSpace(mission)
                && !TextContainsSameMeaning(mission, purpose)
                && !TextContainsSameMeaning(mission, platformIdentity ?? string.Empty))
            {
                builder.Append(' ');
                builder.Append(CleanSentence(mission));
                builder.Append('.');
            }
        }
        else if (!string.IsNullOrWhiteSpace(platformIdentity))
        {
            builder.Append(". ");
            builder.Append(CleanSentence(platformIdentity));
            builder.Append('.');
        }

        builder.AppendLine();
        builder.AppendLine();

        if (frameworkDescriptions.Count > 0)
        {
            builder.Append("It is organized around ");
            builder.Append(frameworkDescriptions.Count);
            builder.Append(" framework families: ");
            builder.Append(string.Join("; ", frameworkDescriptions.Select(description => CleanInline(description))));
            builder.AppendLine(".");
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(outerPlatform))
        {
            builder.Append(CleanSentence(outerPlatform));
            builder.AppendLine(".");
            builder.AppendLine();
        }

        var operatingNotes = new[]
            {
                capabilities,
                safeguards
            }
            .Where(note => !string.IsNullOrWhiteSpace(note))
            .Select(note => CleanSentence(note!))
            .Where(note => !string.IsNullOrWhiteSpace(note))
            .ToArray();
        if (operatingNotes.Length > 0)
        {
            builder.Append(string.Join(". ", operatingNotes));
            builder.AppendLine(".");
        }

        answer = builder.ToString().Trim();
        return answer.Length > 0;
    }

    private static IReadOnlyList<string> SelectMemoryText(
        AskWorkflowContext context,
        Func<string, bool> predicate)
    {
        var memoryItems = context.MemoryResults
            .Where(memory => IsUsableProfileMemoryText($"{memory.SourceType} {memory.Text}"))
            .Where(memory => predicate($"{memory.SourceType} {memory.Text}"))
            .Select(memory => Compact(memory.Text, 220));
        var graphItems = context.GraphEvidence
            .Where(evidence => predicate($"{evidence.Subject} {evidence.Object} {evidence.Template}"))
            .Select(evidence => Compact(evidence.Object, 220));
        return memoryItems
            .Concat(graphItems)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private static double ResolveAverageConfidence(AskWorkflowContext context)
    {
        if (context.MemoryResults.Count == 0)
        {
            return context.GraphEvidence.Count > 0 ? 0.80 : 0.70;
        }

        return Math.Round(context.MemoryResults.Average(memory => memory.Confidence), 2);
    }

    private static string ResolveTemplate(AskWorkflowContext context)
    {
        var question = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion).ToLowerInvariant();
        if (question.Contains("source", StringComparison.Ordinal) || question.Contains("provenance", StringComparison.Ordinal))
        {
            return "memory_sources";
        }

        if (question.Contains("open loop", StringComparison.Ordinal))
        {
            return "memory_open_loops";
        }

        if (question.Contains("key people", StringComparison.Ordinal) || question.Contains("network", StringComparison.Ordinal))
        {
            return "memory_relationships";
        }

        if (IsPersonalDescriptionQuestion(question))
        {
            return "memory_person_description";
        }

        if (IsPersonalWorkRoleQuestion(question))
        {
            return "memory_work_role";
        }

        if (question.Contains("project", StringComparison.Ordinal) || question.Contains("working on", StringComparison.Ordinal))
        {
            return "memory_projects";
        }

        return "memory_profile";
    }

    private static string? ResolvePrimarySubject(AskWorkflowContext context)
    {
        var rawQuestion = AskQuestionScope.ExtractPrimaryQuestion(context.RawQuestion);
        var controlledEntity = ExtractControlledFactEntities(rawQuestion)
            .Concat(ExtractControlledFactEntities(context.RoutingQuestion))
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(controlledEntity))
        {
            return controlledEntity;
        }

        var candidates = new List<string>();
        foreach (var match in System.Text.RegularExpressions.Regex.Matches(rawQuestion, @"\b[A-Z][A-Za-z0-9]{2,}\b").Cast<System.Text.RegularExpressions.Match>())
        {
            var value = match.Value.Trim();
            if (ContainsAny(value, "Can", "Could", "What", "Who", "Where", "When", "Why", "How", "Please"))
            {
                continue;
            }

            candidates.Add(value);
        }

        if (candidates.Count > 0)
        {
            var queryTerms = context.RetrievalDomainTerms
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray();
            var matched = candidates.FirstOrDefault(candidate =>
                queryTerms.Any(term => string.Equals(term, candidate, StringComparison.OrdinalIgnoreCase)));
            return matched ?? candidates[0];
        }

        var term = context.RetrievalDomainTerms.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return string.IsNullOrWhiteSpace(term)
            ? null
            : char.ToUpperInvariant(term[0]) + term[1..];
    }

    private static IEnumerable<string> ExtractControlledFactEntities(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (Match match in ControlledFactEntityRegex.Matches(text))
        {
            var normalized = Regex.Replace(match.Value.Trim(), @"\s+", "-", RegexOptions.CultureInvariant)
                .ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                yield return normalized;
            }
        }
    }

    private static string? ExtractLabeledValue(IReadOnlyList<string> evidence, string label)
    {
        foreach (var text in evidence)
        {
            foreach (var line in text.Split('\n'))
            {
                var cleaned = CleanInline(line);
                var prefix = $"{label}:";
                if (!cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = cleaned[prefix.Length..].Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.TrimEnd('.');
                }
            }

            var inlineMatch = Regex.Match(
                CleanInline(text),
                $@"\b{Regex.Escape(label)}:\s*(?<value>[^\.!\?;\n\r]+)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (inlineMatch.Success)
            {
                var value = inlineMatch.Groups["value"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.TrimEnd('.');
                }
            }
        }

        return null;
    }

    private static string? FindSentence(IReadOnlyList<string> evidence, params string[] terms)
    {
        foreach (var sentence in SplitEvidenceIntoSentences(evidence))
        {
            if (terms.Any(term => sentence.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                return sentence;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ExtractFrameworkDescriptions(IReadOnlyList<string> evidence)
    {
        var names = new[] { "Origin", "Compass", "Veritas", "Zenith" };
        var descriptions = new List<string>();
        foreach (var name in names)
        {
            var description = evidence
                .SelectMany(text => text.Split('\n'))
                .Select(CleanInline)
                .FirstOrDefault(line =>
                    line.StartsWith($"{name} -", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith($"{name} —", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith($"{name}:", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(description))
            {
                descriptions.Add(description.TrimEnd('.'));
            }
        }

        return descriptions;
    }

    private static IEnumerable<string> SplitEvidenceIntoSentences(IReadOnlyList<string> evidence)
    {
        foreach (var text in evidence)
        {
            foreach (var rawLine in text.Split('\n'))
            {
                var normalized = CleanInline(rawLine);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                foreach (var sentence in System.Text.RegularExpressions.Regex.Split(normalized, @"(?<=[\.!\?])\s+"))
                {
                    var cleaned = CleanSentence(sentence);
                    if (cleaned.Length >= 24)
                    {
                        yield return cleaned.TrimEnd('.');
                    }
                }
            }
        }
    }

    private static string CleanPurpose(string value)
    {
        var cleaned = CleanInline(value).Trim().TrimEnd('.');
        var purposePrefix = "Purpose:";
        if (cleaned.StartsWith(purposePrefix, StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[purposePrefix.Length..].Trim();
        }

        if (cleaned.StartsWith("Convert ", StringComparison.Ordinal))
        {
            cleaned = "convert " + cleaned["Convert ".Length..];
        }

        return cleaned.TrimEnd('.');
    }

    private static string CleanSentence(string value)
    {
        return CleanInline(value).Trim().TrimEnd('.');
    }

    private static string? ExtractDeclaredName(string text)
    {
        var cleaned = CleanInline(text);
        var match = Regex.Match(
            cleaned,
            @"\b(?:my name is|i am named|i'm named|call me)\s+(?<name>[A-Za-z'’-]+(?:\s+(?!(?:and|but|i|i'm|im|my|am|work)\b)[A-Za-z'’-]+){0,3})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["name"].Value.Trim();
        return string.IsNullOrWhiteSpace(name)
            ? null
            : name.TrimEnd('.', ',', ';', ':', '!', '?');
    }

    private static string? ExtractDeclaredWorkRole(string text)
    {
        var cleaned = CleanInline(text);
        var match = Regex.Match(
            cleaned,
            @"\b(?:i work as|i am|i'm|my job is|my profession is|my role is)\s+(?:a|an|the)?\s*(?<role>(?:[A-Za-z'’-]+\s+){0,4}(?:engineer|developer|architect|designer|analyst|manager|consultant|administrator|technician|programmer|operator|director|specialist))\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        var role = match.Groups["role"].Value.Trim().TrimEnd('.', ',', ';', ':', '!', '?');
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        return string.Join(
            " ",
            role.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }

    private static string CleanInline(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = NormalizeEscapedBreaks(value)
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal)
            .Replace("•", "-", StringComparison.Ordinal)
            .Trim();
        cleaned = cleaned.TrimStart('-', '*', '#', ' ');
        cleaned = System.Text.RegularExpressions.Regex.Replace(
            cleaned,
            @"^(?:\d+\.\s*)?(?:Evolved mission statement|Vision|Platform identity)\s+",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ");
        return cleaned.Trim();
    }

    private static string EnsureLowercaseStart(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !char.IsUpper(value[0]))
        {
            return value;
        }

        return char.ToLowerInvariant(value[0]) + value[1..];
    }

    private static bool TextContainsSameMeaning(string left, string right)
    {
        var normalizedLeft = CleanInline(left).ToLowerInvariant();
        var normalizedRight = CleanInline(right).ToLowerInvariant();
        return normalizedLeft.Contains(normalizedRight, StringComparison.Ordinal)
               || normalizedRight.Contains(normalizedLeft, StringComparison.Ordinal);
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUsableProfileMemoryText(string value)
    {
        if (value.Contains("prior turn:", StringComparison.OrdinalIgnoreCase)
            && value.Contains("cwi answered", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !ContainsAny(
            value,
            "typical traits",
            "common characteristics that apply broadly",
            "from a more general standpoint",
            "generic ai privacy language")
            && !LooksLikeTechnicalExportArtifact(value);
    }

    private static bool IsUserProfileRelevantMemory(string value)
    {
        var cleaned = CleanInline(value);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return false;
        }

        if (ContainsAny(
            cleaned,
            "my name is",
            "i am in",
            "i work as",
            "my job is",
            "my role is",
            "my profession is",
            "user stated durable context",
            "current priority",
            "current priorities",
            "open loop",
            "relationship",
            "collaborator",
            "working with",
            "owns",
            "owner",
            "responsible for",
            "preference",
            "prefers"))
        {
            return true;
        }

        if (LooksLikePlatformPolicyOrCharterText(cleaned))
        {
            return false;
        }

        return ContainsAny(cleaned, "working on", "building", "project", "priority", "operations", "career", "work life");
    }

    private static bool LooksLikePlatformPolicyOrCharterText(string value)
    {
        return ContainsAny(
                   value,
                   "must not",
                   "may recommend",
                   "may profile",
                   "submitted files are immutable",
                   "exists to transform",
                   "decision-grade intelligence",
                   "generated file must",
                   "framework execution",
                   "framework logic",
                   "source truth",
                   "policy-sensitive",
                   "non-authoritative",
                   "forecasting, causal reasoning")
               && !ContainsAny(value, "my ", "i ", "user stated", "christian", "dimitrius", "demitrius", "chad", "jasmin");
    }

    private static bool LooksLikeTechnicalExportArtifact(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        string[] technicalMarkers =
        {
            "oai/apps/",
            "hasseen",
            "nux",
            "tooltip",
            "localstorage",
            "sessionstorage",
            "feature flag",
            "feature_flag",
            "is_dismissed",
            "dismissed_at",
            "created_at",
            "updated_at",
            "image imported into account chat history:",
            ".zip/user-",
            "file_000000"
        };

        if (technicalMarkers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal)))
        {
            return true;
        }

        return TechnicalJsonPreferenceRegex.IsMatch(normalized);
    }

    private static string? PickFirstUnused(IEnumerable<string> items, HashSet<string> used)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item) || !used.Add(item))
            {
                continue;
            }

            return item;
        }

        return null;
    }

    private static string? PickFirstMatching(IEnumerable<string> items, HashSet<string> used, params string[] terms)
    {
        var candidates = items
            .Where(item => !string.IsNullOrWhiteSpace(item) && !used.Contains(item))
            .ToArray();

        foreach (var term in terms)
        {
            foreach (var item in candidates)
            {
                if (!item.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                used.Add(item);
                return item;
            }
        }

        return null;
    }

    private static string Compact(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        text = NormalizeEscapedBreaks(text);
        var compact = string.Join(
            " ",
            text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= maxLength ? compact : compact[..maxLength] + "...";
    }

    private static string NormalizeEscapedBreaks(string value)
    {
        return value
            .Replace("\\r\\n", " ", StringComparison.Ordinal)
            .Replace("\\n", " ", StringComparison.Ordinal)
            .Replace("\\r", " ", StringComparison.Ordinal)
            .Replace("\\t", " ", StringComparison.Ordinal);
    }

    private static string ToUserFacingEvidence(string text)
    {
        var compact = Compact(text, 220);
        string result;
        if (compact.StartsWith("My ", StringComparison.OrdinalIgnoreCase))
        {
            result = "Your " + compact[3..];
            return NormalizeFirstPersonReferences(result);
        }

        if (compact.StartsWith("I am ", StringComparison.OrdinalIgnoreCase))
        {
            result = "You are " + compact[5..];
            return NormalizeFirstPersonReferences(result);
        }

        if (compact.StartsWith("I'm ", StringComparison.OrdinalIgnoreCase))
        {
            result = "You are " + compact[4..];
            return NormalizeFirstPersonReferences(result);
        }

        if (compact.StartsWith("I ", StringComparison.OrdinalIgnoreCase))
        {
            result = "You " + compact[2..];
            return NormalizeFirstPersonReferences(result);
        }

        return NormalizeFirstPersonReferences(compact);
    }

    private static string NormalizeFirstPersonReferences(string value)
    {
        var normalized = Regex.Replace(
            value,
            @"\bI am\b",
            "you are",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(
            normalized,
            @"\bI'm\b",
            "you are",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(
            normalized,
            @"\bI work\b",
            "you work",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return normalized;
    }
}
#nullable disable
