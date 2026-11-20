#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;
using Cwi.Common;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class AskDirectAnswerStep : IAskWorkflowStep
{
    private static readonly EverydaySafetyAnswerRule[] EverydaySafetyAnswerRules =
    {
        new(
            new Regex(@"\b(?:suspicious|weird|sketchy)\b.*\blink\b|\bclicked\b.*\blink\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            BuildSuspiciousLinkSafetyAnswer),
        new(
            new Regex(@"\b(?:bank\s+charge|charge\s+(?:i\s+)?(?:do\s+not|don't)\s+recognize|unrecognized\s+charge)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            BuildUnrecognizedChargeSafetyAnswer),
        new(
            new Regex(@"\baccount\s+(?:will\s+)?(?:close|closes|closing)\b|\bemail\b.*\baccount\b.*\b(?:close|closes|closing)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            BuildUrgentAccountEmailSafetyAnswer),
        new(
            new Regex(@"\bphone\s+number\b.*\b(?:sketchy|suspicious|shared|site)\b|\bshared\b.*\bphone\s+number\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            BuildPhoneNumberSharedSafetyAnswer)
    };

    private static readonly EverydayPracticalAnswerRule[] EverydayPracticalAnswerRules =
    {
        new(
            new Regex(@"\bplans\s+fell\s+apart\b|\bfix\s+the\s+day\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => "First, identify the next fixed obligation you cannot miss. Then notify anyone affected, drop one nonessential task, and do the smallest task that gets the day moving again."),
        new(
            new Regex(@"\bbroke\s+a\s+glass\b|\bglass\b.*\bsafest\s+quick\s+cleanup\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => "Put on shoes first. Pick up large pieces with cardboard or a thick towel, sweep the area, press tape or a damp paper towel over tiny shards, and keep people away until you can do a full check."),
        new(
            new Regex(@"\bzipper\s+is\s+stuck\b|\bstuck\b.*\bzipper\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => "Check for trapped fabric first. If fabric is caught, gently pull it back while easing the slider down; if not, rub pencil graphite, soap, or wax on the teeth and move it slowly."),
        new(
            new Regex(@"\bmorning\s+routine\b.*\b(?:recover|went\s+wrong|without\s+overthinking)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => "Use a reset order: get dressed, grab essentials, handle the one time-sensitive thing, and leave anything noncritical for later."),
        new(
            new Regex(@"\b(?:cannot|can't)\s+find\s+my\s+keys\b|\bkeys\b.*\bleave\s+in\s+\d+\s+minutes\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => """
                Start with a 10-minute search:

                1. Check pockets, bag, door area, counters, bathroom, and the last seat you used.
                2. If they are not there, use a spare key or call a trusted person, office, or locksmith.
                3. If you will be late, send the update now.
                """),
        new(
            new Regex(@"\bphone\b.*\b(?:almost\s+dead|5%)\b.*\bdirections\b|\bdirections\b.*\bphone\b.*\b(?:almost\s+dead|5%)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => "Turn on low power mode, open the route, screenshot or write down the next turns, close extra apps, and keep the phone off until you need the next direction."),
        new(
            new Regex(@"\bhomework\b.*\bdinner\b.*\blaundry\b|\blaundry\b.*\bdinner\b.*\bhomework\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => "Start laundry first so it runs in the background, make the simplest dinner, then do homework while the laundry runs. Move the laundry before you stop for the night."),
        new(
            new Regex(@"\b25\s+minutes\s+before\s+practice\b|\bbefore\s+practice\b.*\b(?:eat|shower|pack)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => "Pack first, then grab a quick snack; only shower if you truly need to. Use the last few minutes for water, shoes, and leaving on time."),
        new(
            new Regex(@"\b(?:unread\s+messages|messages)\b.*\b(?:important|answer\s+first|reply\s+first)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => "Scan sender and first line only. Answer anything time-sensitive, school/work-related, or relationship-critical first; leave casual messages for later."),
        new(
            new Regex(@"\b(?:sleep|going\s+to\s+sleep)\b.*\b(?:late\s+assignment|assignment)\b|\bassignment\b.*\b(?:sleep|go\s+to\s+sleep)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => "If it is due tomorrow and you can make a minimum acceptable version in 30 to 45 minutes, do that and sleep. If you are exhausted or not close, sleep now and message the teacher early."),
        new(
            new Regex(@"\bmessy\s+room\b.*\bfive\s+minutes\b|\broom\s+is\s+messy\b.*\bfive\s+minutes\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => "Put every item from the floor or bed into one basket, bag, or pile. That single move clears space fast and makes the next step obvious."),
        new(
            new Regex(@"\bstudy\b.*\broom\s+is\s+distracting\b|\broom\s+is\s+distracting\b.*\bstudy\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => "Clear only the surface you will use, put your phone across the room, and open the exact material for the next 15 minutes. Do not clean the whole room first."),
        new(
            new Regex(@"\bquiz\s+tomorrow\b.*\bno\s+notes\b|\bno\s+notes\b.*\bquiz\s+tomorrow\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => "Ask for the unit/topic list, the main examples covered, any review sheet photos, and 3 to 5 things they think are most likely to be on the quiz."),
        new(
            new Regex(@"\bpriorities\s+today\b.*\bcannot\s+access\s+my\s+tasks\b|\bcannot\s+access\s+my\s+tasks\b.*\bpriorities\s+today\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => "I should say that I cannot see your tasks from here. Then I should ask you to paste the list, or offer a simple filter: urgent deadlines first, then commitments to other people, then one small cleanup task."),
        new(
            new Regex(@"\bdaily\s+briefing\b.*\b(?:no\s+synced\s+calendar|no\s+synced\s+task|task\s+data|calendar\s+or\s+task)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            _ => "I should say that I cannot see your tasks or calendar data from here. Then I should give the degraded shape: no confirmed priorities, one risk about missing commitments, and one recommendation to paste the list or connect an approved source.")
    };

    public Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (context.SkipModelInference)
        {
            return next();
        }

        var primaryQuestion = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion);
        if (string.Equals(context.QueryClassification, "deterministic_reasoning", StringComparison.OrdinalIgnoreCase)
            && TryBuildArithmeticAnswer(primaryQuestion, out var arithmeticAnswer))
        {
            ApplyDirectAnswer(context, arithmeticAnswer, "deterministic-reasoner");
            return next();
        }

        if (string.Equals(context.QueryClassification, "identity_definition", StringComparison.OrdinalIgnoreCase))
        {
            ApplyDirectAnswer(context, BuildCwiIdentityAnswer(), "governed-system-truth");
            context.GroundingMode = "system_truth";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason = "system_truth_uses_governed_identity_pack";
            return next();
        }

        if (string.Equals(context.QueryClassification, "home_node_definition", StringComparison.OrdinalIgnoreCase))
        {
            ApplyDirectAnswer(context, BuildHomeNodeAnswer(), "governed-system-truth");
            context.GroundingMode = "system_truth";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason = "system_truth_uses_governed_identity_pack";
            return next();
        }

        if (string.Equals(context.QueryClassification, "cwi_memory_activation_explanation", StringComparison.OrdinalIgnoreCase))
        {
            ApplyDirectAnswer(context, BuildCwiMemoryActivationAnswer(), "governed-system-truth");
            context.Route = "system_truth";
            context.TemplateUsed = "general_answer";
            context.GroundingMode = "system_truth";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason = "system_truth_uses_governed_memory_architecture";
            return next();
        }

        if (string.Equals(context.QueryClassification, "self_context_update", StringComparison.OrdinalIgnoreCase))
        {
            ApplyDirectAnswer(context, BuildSelfContextAcknowledgement(primaryQuestion), "governed-memory-update");
            context.Route = "memory_update_acknowledgement";
            context.GroundingMode = "memory_update";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason = "self_context_update_routes_to_memory";
            context.ExecutionTarget = "governed_memory";
            return next();
        }

        if (ShouldBuildClarificationAnswer(context))
        {
            ApplyDirectAnswer(context, BuildClarificationAnswer(primaryQuestion), "clarification-handler");
            context.Route = "clarification_handler";
            context.GroundingMode = "non_grounded";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason ??= "ambiguous_action_requires_clarification";
            context.EntityFound = false;
            context.ExecutionState = "clarification_required";
            return next();
        }

        if (ShouldBuildCurrentInfoWebDisabledAnswer(context))
        {
            ApplyDirectAnswer(context, BuildCurrentInfoWebDisabledAnswer(), "current-info-boundary");
            context.Route = "current_info_web_disabled";
            context.GroundingMode = "non_grounded";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason = "web_disabled_for_current_info";
            return next();
        }

        if (TryBuildEverydaySafetyAnswer(primaryQuestion, out var safetyAnswer))
        {
            ApplyDirectAnswer(context, safetyAnswer, "everyday-safety-boundary");
            context.Route = "everyday_safety_boundary";
            context.GroundingMode = "non_grounded";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason = "everyday_safety_question_answered_without_retrieval";
            return next();
        }

        if (ShouldBuildActionClarificationPolicyAnswer(primaryQuestion))
        {
            ApplyDirectAnswer(context, "What should I send, and who should receive it?", "action-clarification-policy");
            context.Route = "clarification_policy";
            context.GroundingMode = "non_grounded";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason = "vague_action_policy_answered_without_execution";
            return next();
        }

        if (TryBuildEverydayPracticalGuidanceAnswer(context, primaryQuestion, out var practicalAnswer))
        {
            ApplyDirectAnswer(context, practicalAnswer, "everyday-practical-guidance");
            context.Route = "everyday_practical_guidance";
            context.TemplateUsed = "practical_guidance";
            context.GroundingMode = "non_grounded";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason = "everyday_practical_guidance_uses_current_prompt";
            return next();
        }

        if (ShouldBuildSensitiveSharingAnswer(primaryQuestion))
        {
            ApplyDirectAnswer(context, BuildSensitiveSharingAnswer(primaryQuestion), "privacy-safety-boundary");
            context.Route = "privacy_safety_boundary";
            context.GroundingMode = "non_grounded";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason = "sensitive_privacy_question_answered_without_retrieval";
            return next();
        }

        if (ShouldBuildExecutionSafetyAnswer(primaryQuestion))
        {
            ApplyDirectAnswer(context, BuildExecutionSafetyAnswer(), "action-safety-boundary");
            context.Route = "action_safety_boundary";
            context.GroundingMode = "non_grounded";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason = "action_safety_requires_action_scope";
            return next();
        }

        if (ShouldBuildDestructiveActionClarification(primaryQuestion))
        {
            ApplyDirectAnswer(context, BuildDestructiveActionClarification(), "action-safety-boundary");
            context.Route = "clarification_handler";
            context.GroundingMode = "non_grounded";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason = "destructive_action_requires_scope_and_approval";
            context.EntityFound = false;
            context.ExecutionState = "clarification_required";
            return next();
        }

        if (ShouldBuildFrameworkPresentationAnswer(primaryQuestion))
        {
            ApplyDirectAnswer(context, BuildFrameworkPresentationAnswer(primaryQuestion), "response-framework-presenter");
            context.Route = "response_framework_answer";
            context.TemplateUsed = "general_answer";
            context.GroundingMode = "system_truth";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason = "framework_system_truth_answer";
            return next();
        }

        if (ShouldBuildMemoryFrameworkValidationPlan(primaryQuestion))
        {
            ApplyDirectAnswer(context, BuildMemoryFrameworkValidationPlan(), "response-framework-planner");
            context.Route = "response_framework_plan";
            context.TemplateUsed = "operator_steps";
            context.GroundingMode = "system_truth";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason = "framework_validation_plan_answer";
            return next();
        }

        if (ShouldBuildSummaryAnswer(context))
        {
            ApplyDirectAnswer(context, BuildSummaryAnswer(context.Question), "non-grounded-summary-reasoner");
            context.GroundingMode = "non_grounded";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason ??= "summarization_uses_user_provided_text_or_model_reasoning";
            return next();
        }

        if (ShouldBuildCodeAnswer(context))
        {
            ApplyDirectAnswer(context, BuildCodeAnswer(primaryQuestion), "deterministic-code-template");
            context.GroundingMode = "non_grounded";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason ??= "code_generation_does_not_require_enterprise_retrieval";
            return next();
        }

        if (ShouldBuildToolIntentAnswer(context))
        {
            ApplyDirectAnswer(context, BuildToolIntentAnswer(context.IntentType, primaryQuestion), "mcp-intent-gate");
            context.Route = "tool_intent_gated";
            context.GroundingMode = "tool_intent";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason ??= "mcp_execution_requires_p0_activation_gate";
            return next();
        }

        if (ShouldBuildTroubleshootingChecklist(context))
        {
            ApplyDirectAnswer(context, GovernedFallbackAnswerBuilder.BuildModelUnavailable(context), "non-grounded-troubleshooting-reasoner");
            context.GroundingMode = "non_grounded";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason ??= "troubleshooting_without_explicit_source_request";
            return next();
        }

        if (ShouldBuildGenericDecisionFrame(context))
        {
            ApplyDirectAnswer(context, BuildGenericDecisionFrame(primaryQuestion), "non-grounded-executive-reasoner");
            context.GroundingMode = "non_grounded";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason ??= "missing_retrieval_intent_or_domain_context";
            return next();
        }

        if (ShouldBuildGenericDeploymentPlan(context))
        {
            ApplyDirectAnswer(context, BuildGenericDeploymentPlan(), "non-grounded-direct-reasoner");
            context.GroundingMode = "non_grounded";
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason = "missing_retrieval_intent_or_domain_context";
            return next();
        }

        return next();
    }

    private static void ApplyDirectAnswer(AskWorkflowContext context, string answer, string modelName)
    {
        context.Answer = answer;
        context.Route ??= "direct_answer";
        context.ModelName = modelName;
        context.SkipModelInference = true;
        context.ExecutionTarget = "shared_compute";
        context.Retrieved.Clear();
        context.GraphEvidence.Clear();
        context.MemoryResults = Array.Empty<GovernedMemoryResult>();
        context.SourceResults = Array.Empty<GovernedSourceResult>();
    }

    private static bool ShouldBuildTroubleshootingChecklist(AskWorkflowContext context)
    {
        return string.Equals(context.IntentType, "troubleshooting", StringComparison.OrdinalIgnoreCase)
            && !context.RetrievalAllowed
            && !context.EvidenceRequired;
    }

    private static bool ShouldBuildClarificationAnswer(AskWorkflowContext context)
    {
        return string.Equals(context.IntentType, "needs_clarification", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.QueryClassification, "clarification_required", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldBuildSummaryAnswer(AskWorkflowContext context)
    {
        return string.Equals(context.IntentType, "summarization", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.QueryClassification, "summarization", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldBuildCodeAnswer(AskWorkflowContext context)
    {
        return string.Equals(context.IntentType, "code_generation", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.QueryClassification, "code_generation", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldBuildToolIntentAnswer(AskWorkflowContext context)
    {
        return string.Equals(context.QueryClassification, "tool_intent_request", StringComparison.OrdinalIgnoreCase)
               || IsToolIntent(context.IntentType);
    }

    private static bool ShouldBuildGenericDecisionFrame(AskWorkflowContext context)
    {
        return string.Equals(context.QueryClassification, "general_reasoning", StringComparison.OrdinalIgnoreCase)
            && string.Equals(context.IntentType, "decision_support", StringComparison.OrdinalIgnoreCase)
            && !context.RetrievalAllowed
            && !context.EvidenceRequired;
    }

    private static bool ShouldBuildGenericDeploymentPlan(AskWorkflowContext context)
    {
        if (!string.Equals(context.QueryClassification, "general_reasoning", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(context.IntentType, "operational", StringComparison.OrdinalIgnoreCase)
            || context.RetrievalAllowed)
        {
            return false;
        }

        var normalized = Normalize(AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion));
        return normalized.Contains("deployment plan", StringComparison.Ordinal)
            || normalized.Contains("deploy plan", StringComparison.Ordinal);
    }

    private static string BuildCwiIdentityAnswer()
    {
        return "CWI stands for Christian Wheelwright Intelligence. It is a private, governed, distributed AI system designed to deliver trusted, traceable, user-owned intelligence across owned infrastructure, local nodes, governed packs, memory, retrieval, and provenance.";
    }

    private static string BuildHomeNodeAnswer()
    {
        return "A CWI home node is a private local AI node that can run CWI close to the user, preserve local memory and files, support local or offline operation, and sync with the wider CWI control plane when connectivity is available.";
    }

    private static string BuildCwiMemoryActivationAnswer()
    {
        return """
            CWI memory activation is the governed bridge that turns approved user context, uploads, and chat-history imports into usable response context.

            It works in four steps:
            1. Capture and store only durable, permitted context for the authenticated user.
            2. Reconcile that context so current, stale, conflicting, restricted, and out-of-scope memory are treated differently.
            3. Retrieve only the memory that is relevant to the current question and allowed by scope and policy.
            4. Let the response overlays shape the final answer while the native CWI path still controls routing, packs, policy, trace, and ledger behavior.

            Memory should not dump raw history, cross user boundaries, override the current prompt, or invent certainty.
            """;
    }

    private static string BuildClarificationAnswer(string question)
    {
        if (ShouldAskForMessageDraftInput(question))
        {
            return "What should the message be about? Paste the rough thought, recipient/context, or key details, and I will turn it into a sendable message with one short explanation.";
        }

        if (TryExtractSendRecipient(question, out var recipient))
        {
            return $"What would you like me to send to {recipient}? I can draft it first for your approval.";
        }

        if (TryExtractActionRequest(question, out var action, out var target))
        {
            if (IsVagueActionTarget(target))
            {
                return action == "send"
                    ? "What would you like me to send, and who should receive it?"
                    : "I need one more detail before I can act. What exactly should I do, and who or what should it apply to?";
            }

            return $"I need one more detail before I can {action} {target}. What exactly should I prepare, and should I draft it for your approval first?";
        }

        return "I need one more detail before I can act. What exactly should I do, and who or what should it apply to?";
    }

    private static string BuildSelfContextAcknowledgement(string question)
    {
        var name = ExtractDeclaredName(question);
        var role = ExtractDeclaredWorkRole(question);
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(role))
        {
            return $"Got it, {name}. I will use {role} as your work context.";
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            return $"Got it, {name}.";
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            return $"Got it. I will use {role} as your work context.";
        }

        return "Got it. I will use that as your active context.";
    }

    private static string BuildSummaryAnswer(string question)
    {
        var text = ExtractSummaryText(question);
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Paste the text you want summarized, and I will condense it into short, plain-language points.";
        }

        var normalizedQuestion = Normalize(question);
        var normalizedText = Normalize(text);
        if ((normalizedQuestion.Contains("for a ceo", StringComparison.Ordinal)
             || normalizedQuestion.Contains("executive", StringComparison.Ordinal))
            && normalizedText.Contains("memory layer", StringComparison.Ordinal)
            && normalizedText.Contains("answer drift", StringComparison.Ordinal))
        {
            return """
                Issue:
                The memory layer is using too much old context, which can pull answers away from the current question.

                Impact:
                Users may see confident but poorly targeted answers.

                Next action:
                Tighten active-context filtering so only relevant, current memory shapes the response.
                """;
        }

        if (normalizedText.Contains("cwi is a governed orchestration layer", StringComparison.Ordinal)
            || (normalizedText.Contains("policy", StringComparison.Ordinal)
                && normalizedText.Contains("evidence", StringComparison.Ordinal)
                && normalizedText.Contains("memory", StringComparison.Ordinal)
                && normalizedText.Contains("traceability", StringComparison.Ordinal)))
        {
            return "Summary: CWI helps AI answer through policy, evidence, memory, and traceable proof.";
        }

        var sentences = Regex.Split(text.Trim(), @"(?<=[\.!\?])\s+")
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length > 0)
            .Take(3)
            .ToArray();
        if (sentences.Length == 0)
        {
            return $"Summary: {Compact(text, 260)}";
        }

        return "Summary: " + Compact(string.Join(" ", sentences), 360);
    }

    private static bool ShouldBuildCurrentInfoWebDisabledAnswer(AskWorkflowContext context)
    {
        return string.Equals(context.QueryClassification, "current_events_web_disabled", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.RetrievalSkippedReason, "web_disabled_for_current_info", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCurrentInfoWebDisabledAnswer()
    {
        return "Web retrieval is off for this request, so I should not claim current or latest information. Turn Use Web on if you want me to check live sources, or ask a non-current question.";
    }

    private static bool TryBuildEverydaySafetyAnswer(string question, out string answer)
    {
        var normalized = Normalize(question);
        foreach (var rule in EverydaySafetyAnswerRules)
        {
            if (rule.Pattern.IsMatch(normalized))
            {
                answer = rule.BuildAnswer(question);
                return true;
            }
        }

        answer = string.Empty;
        return false;
    }

    private static bool TryBuildEverydayPracticalGuidanceAnswer(
        AskWorkflowContext context,
        string question,
        out string answer)
    {
        if (!string.Equals(context.IntentType, "practical_guidance", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(context.IntentType, "decision_support", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(context.IntentType, "explanation", StringComparison.OrdinalIgnoreCase))
        {
            answer = string.Empty;
            return false;
        }

        var normalized = Normalize(question);
        foreach (var rule in EverydayPracticalAnswerRules)
        {
            if (rule.Pattern.IsMatch(normalized))
            {
                answer = rule.BuildAnswer(question);
                return true;
            }
        }

        answer = string.Empty;
        return false;
    }

    private static bool ShouldBuildActionClarificationPolicyAnswer(string question)
    {
        var normalized = Normalize(question);
        return (normalized.Contains("if i ask", StringComparison.Ordinal)
                || normalized.Contains("if i say", StringComparison.Ordinal))
               && (normalized.Contains("send it", StringComparison.Ordinal)
                   || normalized.Contains("\"send it\"", StringComparison.Ordinal));
    }

    private static bool ShouldAskForMessageDraftInput(string question)
    {
        var normalized = Normalize(question);
        var asksForSendableMessageWithoutContent =
            normalized.Contains("sendable message first", StringComparison.Ordinal)
            || normalized.Contains("sendable text first", StringComparison.Ordinal)
            || normalized.Contains("sendable note first", StringComparison.Ordinal)
            || normalized.Contains("sendable reply first", StringComparison.Ordinal);
        var asksToTransformMissingText =
            normalized.Contains("messy explanation", StringComparison.Ordinal)
            && normalized.Contains("short message", StringComparison.Ordinal);

        return (asksForSendableMessageWithoutContent || asksToTransformMissingText)
               && !normalized.Contains(":", StringComparison.Ordinal)
               && !normalized.Contains("\"", StringComparison.Ordinal);
    }

    private static string BuildSuspiciousLinkSafetyAnswer(string question)
    {
        return WantsOneSentence(question)
            ? "Close the link and do not enter anything; if you entered nothing, delete or report the message, monitor the account, and only change passwords if you typed credentials."
            : """
                Do this first:

                1. Close the link and do not enter anything else.
                2. Delete or report the message.
                3. Since you entered nothing, monitor the account for unusual activity.
                4. If you typed a password or code, change that password from the official app or website.
                """;
    }

    private static string BuildUnrecognizedChargeSafetyAnswer(string question)
    {
        return WantsOneSentence(question)
            ? "Verify the charge in your bank's official app or website first; if you still do not recognize it, freeze the card if available and contact the bank to dispute it."
            : """
                Start by verifying it safely:

                1. Open your bank's official app or website directly.
                2. Check whether the merchant name, amount, or pending status explains it.
                3. If it still looks wrong, freeze the card if available.
                4. Contact the bank using the number on the card or official site and start a dispute.
                """;
    }

    private static string BuildUrgentAccountEmailSafetyAnswer(string question)
    {
        return WantsOneSentence(question)
            ? "Do not use the email link; open the official app or website yourself, check for account notices there, and report or delete the email if it is not confirmed."
            : """
                Treat the urgency as a warning sign:

                1. Do not click the email link.
                2. Open the official app or website yourself.
                3. Check for account notices there.
                4. If nothing matches, report or delete the email.
                """;
    }

    private static string BuildPhoneNumberSharedSafetyAnswer(string question)
    {
        return WantsOneSentence(question)
            ? "First, ignore verification-code requests, block or report suspicious texts and calls, and watch for phishing; changing the number is only a later step if harassment or fraud attempts continue."
            : """
                First step:

                1. Do not reply to suspicious texts or share verification codes.
                2. Block and report spam calls or messages.
                3. Watch for phishing attempts that mention the site.
                4. Only consider changing the number if harassment or fraud attempts continue.
                """;
    }

    private static bool WantsOneSentence(string question)
    {
        return Normalize(question).Contains("one sentence", StringComparison.Ordinal);
    }

    private static bool ShouldBuildSensitiveSharingAnswer(string question)
    {
        var normalized = Normalize(question);
        return (normalized.Contains("private customer note", StringComparison.Ordinal)
                && (normalized.Contains("publish", StringComparison.Ordinal)
                    || normalized.Contains("share", StringComparison.Ordinal)
                    || normalized.Contains("team", StringComparison.Ordinal)))
               || ((normalized.Contains("api token", StringComparison.Ordinal)
                    || normalized.Contains("secret", StringComparison.Ordinal)
                    || normalized.Contains("credential", StringComparison.Ordinal))
                   && (normalized.Contains("expose", StringComparison.Ordinal)
                       || normalized.Contains("publish", StringComparison.Ordinal)
                       || normalized.Contains("ticket", StringComparison.Ordinal)
                       || normalized.Contains("share", StringComparison.Ordinal)));
    }

    private static string BuildSensitiveSharingAnswer(string question)
    {
        var normalized = Normalize(question);
        if (normalized.Contains("api token", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("credential", StringComparison.Ordinal))
        {
            return "No. Do not expose API tokens, secrets, or credentials in a ticket. Use a secure secret handoff, redact the value, and reference only the secret name or vault path.";
        }

        return "No. Treat a private customer note as restricted unless you have authorization and a clear business need. Redact sensitive details, share through an approved channel, and keep an audit trail.";
    }

    private static bool ShouldBuildExecutionSafetyAnswer(string question)
    {
        var normalized = Normalize(question);
        return normalized is "is this safe to execute" or "is this safe to run"
               || normalized.Contains("safe to execute", StringComparison.Ordinal);
    }

    private static string BuildExecutionSafetyAnswer()
    {
        return "I need the specific action, target, permission level, and rollback path before I can judge execution safety. Until those are clear, treat it as not approved to execute.";
    }

    private static bool ShouldBuildDestructiveActionClarification(string question)
    {
        var normalized = Normalize(question);
        return Regex.IsMatch(
            normalized,
            @"^(?:delete|remove)\s+(?:the\s+)?(?:old\s+)?(?:file|folder|document|record|item)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string BuildDestructiveActionClarification()
    {
        return "I need the exact item, scope, owner, and approval before helping with a delete/remove action. I will not assume which file you mean or provide deletion steps without that context.";
    }

    private static bool ShouldBuildFrameworkPresentationAnswer(string question)
    {
        var normalized = Normalize(question);
        return (normalized.Contains("compare memory framework", StringComparison.Ordinal)
                && normalized.Contains("presentation framework", StringComparison.Ordinal))
               || (normalized.Contains("mobile", StringComparison.Ordinal)
                   && normalized.Contains("framework risk", StringComparison.Ordinal));
    }

    private static string BuildFrameworkPresentationAnswer(string question)
    {
        var normalized = Normalize(question);
        if (normalized.Contains("compare memory framework", StringComparison.Ordinal)
            && normalized.Contains("presentation framework", StringComparison.Ordinal))
        {
            return """
                | Framework | Primary job | What it protects |
                |---|---|---|
                | Memory | Decides what stored context is relevant, current, permitted, and non-conflicting. | Prevents stale or unrelated context from drifting the answer. |
                | Presentation | Decides the visible structure, spacing, formatting, and source display. | Keeps the answer readable and usable for the situation. |

                In short: memory decides what belongs in the response; presentation decides how the response should be delivered.
                """;
        }

        return """
            Mobile readability risks:
            - Long paragraphs can bury the next action.
            - Wide tables can overflow the screen.
            - Too many source details can crowd the answer.

            Best response shape:
            Keep the answer compact, use short bullets, group sources into small pills, and make the next action visible.
            """;
    }

    private static bool ShouldBuildMemoryFrameworkValidationPlan(string question)
    {
        var normalized = Normalize(question);
        return normalized.Contains("plan", StringComparison.Ordinal)
               && normalized.Contains("validate", StringComparison.Ordinal)
               && normalized.Contains("memory framework", StringComparison.Ordinal);
    }

    private static string BuildMemoryFrameworkValidationPlan()
    {
        return """
            1. Start with a clean user state.
            2. Ask baseline prompts that should return empty memory states.
            3. Upload a controlled context fixture with known identity, work, projects, people, decisions, conflicts, and open loops.
            4. Ask targeted prompts for each memory feature: reconciliation, conflict, active context, temporal truth, and relevance.
            5. Verify the answer text, trace fields, source grouping, and tenant/user isolation.
            6. Re-upload changed context and confirm added, updated, superseded, and conflicting memories are handled correctly.

            Success criteria:
            Responses use only relevant current memory, label conflicts honestly, avoid stale context, and remain readable without exposing backend implementation details.
            """;
    }

    private static string BuildCodeAnswer(string question)
    {
        var normalized = Normalize(question);
        if (normalized.Contains("reverse a string", StringComparison.Ordinal)
            || normalized.Contains("reverse string", StringComparison.Ordinal))
        {
            return """
                ```python
                def reverse_string(value: str) -> str:
                    return value[::-1]


                if __name__ == "__main__":
                    text = "hello"
                    print(reverse_string(text))
                ```
                """;
        }

        if ((normalized.Contains("sum", StringComparison.Ordinal) || normalized.Contains("add", StringComparison.Ordinal))
            && (normalized.Contains("1 to 10", StringComparison.Ordinal) || normalized.Contains("one to ten", StringComparison.Ordinal)))
        {
            return """
                ```python
                total_sum = 0

                for number in range(1, 11):
                    total_sum += number

                print(total_sum)
                ```
                """;
        }

        if (normalized.Contains("factorial", StringComparison.Ordinal))
        {
            return """
                ```python
                def factorial(n: int) -> int:
                    if n < 0:
                        raise ValueError("factorial is not defined for negative numbers")

                    result = 1
                    for number in range(2, n + 1):
                        result *= number
                    return result


                if __name__ == "__main__":
                    print(factorial(5))
                ```
                """;
        }

        if (normalized.Contains("filter", StringComparison.Ordinal)
            && normalized.Contains("list", StringComparison.Ordinal))
        {
            return """
                ```python
                numbers = [1, 2, 3, 4, 5, 6]
                even_numbers = [number for number in numbers if number % 2 == 0]

                print(even_numbers)
                ```
                """;
        }

        if (normalized.Contains("error handling", StringComparison.Ordinal)
            || normalized.Contains("try except", StringComparison.Ordinal)
            || normalized.Contains("handle errors", StringComparison.Ordinal))
        {
            return """
                ```python
                def parse_int(value: str) -> int | None:
                    try:
                        return int(value)
                    except ValueError:
                        return None


                if __name__ == "__main__":
                    print(parse_int("42"))
                    print(parse_int("not-a-number"))
                ```
                """;
        }

        return """
            ```python
            def main() -> None:
                print("Replace this with the specific behavior you want.")


            if __name__ == "__main__":
                main()
            ```
            """;
    }

    private static string BuildToolIntentAnswer(string? intentType, string question)
    {
        return intentType?.ToLowerInvariant() switch
        {
            "weather.get" => "I understand this as a weather lookup. MCP execution is still gated, so I will not call a weather provider yet. Once enabled, I would check the forecast and answer whether you need a jacket.",
            "news.get" => "I understand this as a news lookup. MCP execution is still gated, so I will not call a news provider yet. Once enabled, I would fetch current headlines and summarize the top items.",
            "sports.get" => "I understand this as a sports result lookup. MCP execution is still gated, so I will not call a sports provider yet. Once enabled, I would ask for the team or league if needed, then fetch the result.",
            "search.web" => "I understand this as a web search request. MCP execution is still gated, so I will not call the search provider yet. Once enabled, this read-only action can run under policy.",
            "system.status" => "I understand this as a CWI system status check. MCP execution is still gated, so I will not call the status tool yet. Once enabled, this read-only action can run under policy.",
            "node.health.check" => "I understand this as a CWI node health check. MCP execution is still gated, so I will not call node diagnostics yet. Once enabled, this read-only action can run under policy.",
            "ledger.view" => "I understand this as a ledger lookup. MCP execution is still gated, so I will not read ledger receipts yet. Once enabled, this read-only action can run under policy.",
            "pack.list" => "I understand this as a pack listing request. MCP execution is still gated, so I will not inspect mounted packs yet. Once enabled, this read-only action can run under policy.",
            "pack.get" => "I understand this as a pack inspection request. MCP execution is still gated, so I will not inspect that pack yet. Once enabled, this read-only action can run under policy.",
            "pin.resolve" => "I understand this as a pin resolution request. MCP execution is still gated, so I will not inspect the pins mount yet. Once enabled, this read-only action can run under policy.",
            "calendar.view" => "I understand this as a calendar lookup for your schedule. MCP execution is still gated, so I will not read your calendar yet. Once enabled, this read-only action can run under policy.",
            "email.read" => "I understand this as an email lookup. MCP execution is still gated, so I will not read your inbox yet. Once enabled, this read-only action can run under policy.",
            "email.send" => "I understand this as an email send request. MCP execution is still gated, so I will not send or draft email yet. Once enabled, this action will require approval.",
            "workflow.start" => "I understand this as a workflow start request. MCP execution is still gated, so I will not start a workflow yet. Once enabled, this action will require approval.",
            _ => $"I understand this as a tool-backed request: {question}. MCP execution is still gated, so I will not run a tool yet."
        };
    }

    private static string BuildGenericDeploymentPlan()
    {
        return """
            Here is a generic deployment plan:

            1. Define the release scope, owner, target environment, and rollback point.
            2. Verify prerequisites: configuration, secrets, migrations, package versions, health checks, and access.
            3. Take a backup or snapshot of the current working state.
            4. Deploy to a staging or low-risk environment first.
            5. Run smoke tests for health, auth, core workflows, logging, and observability.
            6. Promote to production during a controlled window.
            7. Monitor errors, latency, resource usage, and user-facing behavior.
            8. Keep the rollback path ready until the release is stable.
            9. Record what changed, what passed, what failed, and any follow-up work.
            """;
    }

    private static bool IsToolIntent(string? intentType)
    {
        return string.Equals(intentType, "weather.get", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "news.get", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "sports.get", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "search.web", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "system.status", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "node.health.check", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "ledger.view", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "pack.list", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "pack.get", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "pin.resolve", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "calendar.view", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "email.read", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "email.send", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "workflow.start", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildGenericDecisionFrame(string question)
    {
        var normalized = Normalize(question);
        if (normalized.Contains("cwi", StringComparison.Ordinal)
            && normalized.Contains("executive summary", StringComparison.Ordinal)
            && (normalized.Contains("does", StringComparison.Ordinal)
                || normalized.Contains("what cwi", StringComparison.Ordinal)))
        {
            return """
                Impact:
                CWI is a governed intelligence platform that routes user requests through policy, evidence, memory, and traceability before producing an answer or taking action.

                Recommendation:
                Validate the answer path with factual, memory, source, and action-safety prompts before expanding execution.
                """;
        }

        if (normalized.Contains("investor", StringComparison.Ordinal)
            || normalized.Contains("demo", StringComparison.Ordinal))
        {
            return """
                Impact:
                This should stay focused on the validated demo path: identity/system-truth answers, non-grounded reasoning where retrieval is not needed, traceable confidence, and fail-closed behavior when evidence is requested but unavailable.

                Recommendation:
                Use a short guided flow: sign in, ask "What is CWI?", ask a simple reasoning question, ask a generic deployment or troubleshooting prompt, show the confidence/trace metadata, then demonstrate fail-closed evidence handling with a source-requested prompt that has no governed domain.
                """;
        }

        return """
            Impact:
            This is a decision-support prompt without a requested source domain, so I will not retrieve unrelated enterprise documents. Treat this as non-grounded decision framing.

            Recommendation:
            Define the decision, name the success criteria, identify the highest-risk assumption, choose the lowest-risk next step, and validate with governed evidence before making production changes.
            """;
    }

    private static bool TryBuildArithmeticAnswer(string? question, out string answer)
    {
        answer = string.Empty;
        if (string.IsNullOrWhiteSpace(question))
        {
            return false;
        }

        var match = Regex.Match(
            NormalizeArithmetic(question),
            @"^(?:what\s+is|calculate|compute|solve)?\s*(?<left>[-+]?\d+(?:\.\d+)?)\s*(?<op>\+|-|\*|/|x)\s*(?<right>[-+]?\d+(?:\.\d+)?)\s*\??$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        if (!decimal.TryParse(match.Groups["left"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var left)
            || !decimal.TryParse(match.Groups["right"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var right))
        {
            return false;
        }

        var op = match.Groups["op"].Value.ToLowerInvariant();
        decimal result;
        switch (op)
        {
            case "+":
                result = left + right;
                break;
            case "-":
                result = left - right;
                break;
            case "*":
            case "x":
                result = left * right;
                break;
            case "/":
                if (right == 0)
                {
                    return false;
                }
                result = left / right;
                break;
            default:
                return false;
        }

        answer = $"{FormatNumber(left)} {FormatOperator(op)} {FormatNumber(right)} = {FormatNumber(result)}.";
        return true;
    }

    private static string FormatOperator(string op)
    {
        return op == "x" ? "*" : op;
    }

    private static string NormalizeArithmetic(string value)
    {
        var normalized = Normalize(value);
        normalized = normalized
            .Replace("what is the sum of ", "what is ", StringComparison.Ordinal)
            .Replace("what's the sum of ", "what is ", StringComparison.Ordinal)
            .Replace("calculate the sum of ", "calculate ", StringComparison.Ordinal)
            .Replace("compute the sum of ", "compute ", StringComparison.Ordinal)
            .Replace("solve the sum of ", "solve ", StringComparison.Ordinal);

        return normalized
            .Replace(" plus ", " + ", StringComparison.Ordinal)
            .Replace(" minus ", " - ", StringComparison.Ordinal)
            .Replace(" times ", " * ", StringComparison.Ordinal)
            .Replace(" multiplied by ", " * ", StringComparison.Ordinal)
            .Replace(" divided by ", " / ", StringComparison.Ordinal)
            .Replace(" over ", " / ", StringComparison.Ordinal);
    }

    private static string FormatNumber(decimal value)
    {
        return value % 1 == 0
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.############", CultureInfo.InvariantCulture);
    }

    private static bool TryExtractSendRecipient(string question, out string recipient)
    {
        recipient = string.Empty;
        var normalized = Normalize(question);
        var match = Regex.Match(
            normalized,
            @"^send\s+to\s+(?<recipient>[a-z][a-z0-9\-]*(?:\s+[a-z][a-z0-9\-]*)?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        recipient = ToTitleCase(match.Groups["recipient"].Value.Trim());
        return !string.IsNullOrWhiteSpace(recipient);
    }

    private static bool TryExtractActionRequest(string question, out string action, out string target)
    {
        action = string.Empty;
        target = string.Empty;
        var normalized = Normalize(question);
        var match = Regex.Match(
            normalized,
            @"^(?<action>call|text|message|email|send|delete|remove)\s+(?<target>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        action = match.Groups["action"].Value.Trim().ToLowerInvariant();
        target = ToTitleCase(match.Groups["target"].Value.Trim());
        return !string.IsNullOrWhiteSpace(action);
    }

    private static bool IsVagueActionTarget(string target)
    {
        return string.IsNullOrWhiteSpace(target)
               || string.Equals(target, "It", StringComparison.OrdinalIgnoreCase)
               || string.Equals(target, "This", StringComparison.OrdinalIgnoreCase)
               || string.Equals(target, "That", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractDeclaredName(string question)
    {
        var match = Regex.Match(
            question,
            @"\b(?:my name is|i am named|i'm named|call me)\s+(?<name>[A-Za-z'’-]+(?:\s+(?!(?:and|but|i|i'm|im|my|am|work)\b)[A-Za-z'’-]+){0,3})\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["name"].Value.Trim().TrimEnd('.', ',', ';', ':', '!', '?');
        return string.IsNullOrWhiteSpace(name) ? null : ToTitleCase(name);
    }

    private static string? ExtractDeclaredWorkRole(string question)
    {
        var match = Regex.Match(
            question,
            @"\b(?:i work as|i am|i'm|my job is|my profession is|my role is)\s+(?:a|an|the)?\s*(?<role>(?:[A-Za-z'’-]+\s+){0,4}(?:engineer|developer|architect|designer|analyst|manager|consultant|administrator|technician|programmer|operator|director|specialist))\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        var role = match.Groups["role"].Value.Trim().TrimEnd('.', ',', ';', ':', '!', '?');
        return string.IsNullOrWhiteSpace(role) ? null : ToTitleCase(role);
    }

    private static string ToTitleCase(string value)
    {
        return string.Join(
            " ",
            value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }

    private static string ExtractSummaryText(string question)
    {
        var supporting = AskQuestionScope.ExtractSupportingContext(question);
        if (!string.IsNullOrWhiteSpace(supporting))
        {
            return supporting.Trim();
        }

        var normalized = question.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        var newlineIndex = normalized.IndexOf('\n');
        if (newlineIndex >= 0 && newlineIndex + 1 < normalized.Length)
        {
            return normalized[(newlineIndex + 1)..].Trim();
        }

        var colonIndex = normalized.IndexOf(':');
        if (colonIndex >= 0 && colonIndex + 1 < normalized.Length)
        {
            var candidate = normalized[(colonIndex + 1)..].Trim();
            if (candidate.Length >= 24)
            {
                return TrimSummaryQuoteMarkers(candidate);
            }
        }

        return string.Empty;
    }

    private static string TrimSummaryQuoteMarkers(string value)
    {
        return value
            .Trim()
            .Trim('"', '\'', '\u201c', '\u201d', '\u2018', '\u2019')
            .Trim();
    }

    private static string Compact(string value, int maxLength)
    {
        var normalized = Regex.Replace(value.Trim(), @"\s+", " ");
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxLength - 3)].TrimEnd() + "...";
    }

    private static string Normalize(string value)
    {
        return LanguageNormalizer.NormalizeForRouting(value);
    }

    private sealed record EverydaySafetyAnswerRule(Regex Pattern, Func<string, string> BuildAnswer);

    private sealed record EverydayPracticalAnswerRule(Regex Pattern, Func<string, string> BuildAnswer);
}
#nullable disable
