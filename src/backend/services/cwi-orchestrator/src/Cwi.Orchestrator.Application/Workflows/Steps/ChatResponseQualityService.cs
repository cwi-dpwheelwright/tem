#nullable enable
using System.Text.RegularExpressions;
using Cwi.Orchestrator.Application.Workflows;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class ChatResponseQualityService
{
    public const string Version = "chat-response-quality-v1";

    private static readonly Regex InternalProvenancePrefaceRegex = new(
        @"^\s*(?:here\s+is|here's|based\s+on|from)\b[^\n]{0,220}?\b(?:activated|imported|uploaded|stored|governed)(?:\s+(?:activated|imported|uploaded|stored|governed|user))*\s+(?:memory|context|chat\s+history|sources?)\b[^\n:]*:\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex InternalProvenanceOpeningRegex = new(
        @"^\s*(?:based\s+on|from)\s+(?:your\s+)?(?:activated|imported|uploaded|stored|governed)(?:\s+(?:activated|imported|uploaded|stored|governed|user))*\s+(?:memory|context|chat\s+history|sources?)\b(?:,|\.)?\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BlankLineRegex = new(
        @"\n{3,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MessageDraftOpeningRegex = new(
        "^\\s*(?:(?:sure|absolutely|of course)[,\\s]+)?(?:(?:here(?:'|\\u2019)s|here is)\\s+(?:a|an|the)?\\s*(?:short\\s+|polite\\s+|simple\\s+|possible\\s+)?(?:message|text|draft|version|way\\s+to\\s+say\\s+it)[^:\\n]{0,140}:|(?:draft\\s+message|message|text|draft|you\\s+could\\s+say|try\\s+this):)\\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public ChatResponseQualityDecision Apply(AskWorkflowContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var profile = ResolveProfile(context);
        var budget = ResolveContextBudget(context);
        var polished = PolishFinalAnswer(context.Answer, context, out var polishApplied);
        context.Answer = polished;

        return new ChatResponseQualityDecision(
            FrameworkVersion: Version,
            Profile: profile.Mode,
            ProfileReason: profile.Reason,
            ContextBudgetMode: budget.Mode,
            ContextBudgetReason: budget.Reason,
            FinalPolishApplied: polishApplied ? true : null);
    }

    private static ProfileDecision ResolveProfile(AskWorkflowContext context)
    {
        if (string.Equals(context.IntentType, "needs_clarification", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "clarification_required", StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileDecision("chat_clarification", "profile_clarification_requires_single_clear_question");
        }

        if (string.Equals(context.McpPolicyDecision, "blocked", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.McpPolicyDecision, "approval_required", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.ExecutionState, "blocked_by_policy", StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileDecision("chat_action_boundary", "profile_policy_or_approval_boundary");
        }

        if (string.Equals(context.PresentationTemplateFamily, "code", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "code_generation", StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileDecision("chat_code", "profile_code_requires_preserved_runnable_format");
        }

        if (string.Equals(context.IntentType, "message_draft", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.PresentationTemplateFamily, "message_draft", StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileDecision("chat_message_draft", "profile_message_draft_requires_sendable_text_first");
        }

        if (string.Equals(context.PresentationTemplateFamily, "risk", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.PresentationTemplateFamily, "daily_briefing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.PresentationTemplateFamily, "what_changed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.PresentationTemplateFamily, "plan", StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileDecision("chat_structured", "profile_response_mode_controls_shape");
        }

        if (string.Equals(context.ContextAwarenessMode, "memory", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.PresentationTemplateFamily, "memory_backed", StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileDecision("chat_memory_recall", "profile_memory_must_be_relevant_and_user_facing");
        }

        if (HasSourceContext(context))
        {
            return new ProfileDecision("chat_source_backed", "profile_sources_support_unified_answer");
        }

        if (string.Equals(context.PresentationTemplateFamily, "direct_answer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "deterministic_reasoning", StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileDecision("chat_direct", "profile_simple_questions_stay_short");
        }

        if (string.Equals(context.VerbosityMode, "step_by_step", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.PresentationTemplateFamily, "operator_steps", StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileDecision("chat_practical_guidance", "profile_actionable_guidance_prefers_steps");
        }

        if (string.Equals(context.IntentType, "practical_guidance", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.PresentationTemplateFamily, "practical_guidance", StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileDecision("chat_practical_guidance", "profile_everyday_guidance_requires_concrete_next_step");
        }

        return new ProfileDecision("chat_default", "profile_default_helpful_chat");
    }

    private static ContextBudgetDecision ResolveContextBudget(AskWorkflowContext context)
    {
        if (string.Equals(context.QueryClassification, "deterministic_reasoning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.PresentationTemplateFamily, "direct_answer", StringComparison.OrdinalIgnoreCase))
        {
            return new ContextBudgetDecision("context_budget_minimal", "budget_simple_answer_ignores_unneeded_memory");
        }

        if (string.Equals(context.ContextAwarenessMode, "memory", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.PresentationTemplateFamily, "memory_backed", StringComparison.OrdinalIgnoreCase))
        {
            return new ContextBudgetDecision("context_budget_memory_scoped", "budget_memory_only_when_relevant_current_and_permitted");
        }

        if (HasSourceContext(context))
        {
            return new ContextBudgetDecision("context_budget_source_scoped", "budget_sources_are_supporting_evidence_not_answer_structure");
        }

        return new ContextBudgetDecision("context_budget_default", "budget_current_question_leads");
    }

    private static string? PolishFinalAnswer(string? answer, AskWorkflowContext context, out bool polishApplied)
    {
        polishApplied = false;
        if (string.IsNullOrWhiteSpace(answer))
        {
            return answer;
        }

        var shaped = answer.Trim();
        var original = shaped;

        if (!ContainsCodeFence(shaped))
        {
            shaped = NormalizeEscapedFormatting(shaped);
            shaped = NormalizeEncodingArtifacts(shaped);
        }

        shaped = ReplaceInternalProvenancePreface(shaped, context, out var prefaceRewritten);
        shaped = StripMessageDraftOpening(shaped, context, out var messageWrapperStripped);
        shaped = NormalizeBlankLines(shaped);
        if (string.IsNullOrWhiteSpace(shaped) && !string.IsNullOrWhiteSpace(original))
        {
            shaped = BuildEmptyAfterPolishFallback(context);
        }

        polishApplied = prefaceRewritten
                        || messageWrapperStripped
                        || !string.Equals(original, shaped, StringComparison.Ordinal);
        return shaped.Trim();
    }

    private static string NormalizeEscapedFormatting(string answer)
    {
        if (!answer.Contains("\\n", StringComparison.Ordinal)
            && !answer.Contains("\\r", StringComparison.Ordinal)
            && !answer.Contains("\\t", StringComparison.Ordinal))
        {
            return answer;
        }

        return answer
            .Replace("\\r\\n", "\n", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\r", "\n", StringComparison.Ordinal)
            .Replace("\\t", "  ", StringComparison.Ordinal);
    }

    private static string NormalizeEncodingArtifacts(string answer)
    {
        if (!answer.Contains('\u00e2', StringComparison.Ordinal)
            && !answer.Contains('\u00c2', StringComparison.Ordinal))
        {
            return answer;
        }

        return answer
            .Replace("\u00e2\u20ac\u2122", "'", StringComparison.Ordinal)
            .Replace("\u00e2\u20ac\u02dc", "'", StringComparison.Ordinal)
            .Replace("\u00e2\u20ac\u0153", "\"", StringComparison.Ordinal)
            .Replace("\u00e2\u20ac\u009d", "\"", StringComparison.Ordinal)
            .Replace("\u00e2\u20ac\u201d", " - ", StringComparison.Ordinal)
            .Replace("\u00e2\u20ac\u201c", " - ", StringComparison.Ordinal)
            .Replace("\u00e2\u20ac\u00a6", "...", StringComparison.Ordinal)
            .Replace("\u00e2\u20ac\u00a2", "-", StringComparison.Ordinal)
            .Replace("\u00c2\u00a0", " ", StringComparison.Ordinal);
    }

    private static string ReplaceInternalProvenancePreface(
        string answer,
        AskWorkflowContext context,
        out bool rewritten)
    {
        rewritten = false;
        var match = InternalProvenancePrefaceRegex.Match(answer);
        if (!match.Success || match.Index != 0)
        {
            match = InternalProvenanceOpeningRegex.Match(answer);
        }

        if (!match.Success || match.Index != 0)
        {
            return answer;
        }

        var remainder = answer[match.Length..].TrimStart();
        var replacement = IsMemoryProfileAnswer(context)
            ? "Here is what I understand:"
            : "Here is what I found:";
        rewritten = true;
        return string.IsNullOrWhiteSpace(remainder)
            ? replacement
            : $"{replacement}\n\n{remainder}";
    }

    private static string StripMessageDraftOpening(
        string answer,
        AskWorkflowContext context,
        out bool stripped)
    {
        stripped = false;
        if (!IsMessageDraftAnswer(context))
        {
            return answer;
        }

        var shaped = answer.TrimStart();
        for (var i = 0; i < 3; i++)
        {
            var match = MessageDraftOpeningRegex.Match(shaped);
            if (!match.Success || match.Index != 0)
            {
                break;
            }

            shaped = shaped[match.Length..].TrimStart();
            stripped = true;
        }

        return shaped;
    }

    private static string NormalizeBlankLines(string answer)
    {
        return BlankLineRegex.Replace(answer.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'), "\n\n");
    }

    private static string BuildEmptyAfterPolishFallback(AskWorkflowContext context)
    {
        if (IsMemoryProfileAnswer(context) || context.MemoryResults.Count > 0)
        {
            return "I found relevant account context, but I could not turn it into a clean answer for this question.";
        }

        return "I could not turn the available context into a clean answer for this question.";
    }

    private static bool IsMemoryProfileAnswer(AskWorkflowContext context)
    {
        return string.Equals(context.IntentType, "memory_recall", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.QueryClassification, "memory_graph_request", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.PresentationTemplateFamily, "memory_backed", StringComparison.OrdinalIgnoreCase)
               || (context.TemplateUsed?.StartsWith("memory_", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool IsMessageDraftAnswer(AskWorkflowContext context)
    {
        return string.Equals(context.IntentType, "message_draft", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.PresentationTemplateFamily, "message_draft", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.ChatResponseProfile, "chat_message_draft", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSourceContext(AskWorkflowContext context)
    {
        return context.TrustedWebResults.Count > 0
               || context.MemoryResults.Count > 0
               || context.MemoryConflictSummaries.Count > 0
               || context.SourceResults.Count > 0
               || context.Retrieved.Count > 0
               || context.GraphEvidence.Count > 0
               || context.McpResult is not null;
    }

    private static bool ContainsCodeFence(string answer)
    {
        return answer.Contains("```", StringComparison.Ordinal);
    }

    private sealed record ProfileDecision(string Mode, string Reason);
    private sealed record ContextBudgetDecision(string Mode, string Reason);
}

public sealed record ChatResponseQualityDecision(
    string FrameworkVersion,
    string Profile,
    string ProfileReason,
    string ContextBudgetMode,
    string ContextBudgetReason,
    bool? FinalPolishApplied);
#nullable disable
