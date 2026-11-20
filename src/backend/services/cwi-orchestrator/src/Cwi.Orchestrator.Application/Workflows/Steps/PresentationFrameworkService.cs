#nullable enable
using Cwi.Orchestrator.Application.Workflows;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class PresentationFrameworkService
{
    public const string Version = "presentation-framework-v1";

    public PresentationDecision Evaluate(AskWorkflowContext context, string answer)
    {
        var templateFamily = ResolveTemplateFamily(context);
        var rhythm = ResolveRhythm(context, templateFamily, answer);
        var formatting = ResolveFormatting(context, templateFamily, answer);
        var sourceDisplay = ResolveSourceDisplay(context);

        return new PresentationDecision(
            FrameworkVersion: Version,
            TemplateFamily: templateFamily,
            TemplateReason: $"template_{templateFamily}",
            RhythmMode: rhythm.Mode,
            RhythmReason: rhythm.Reason,
            FormattingMode: formatting.Mode,
            FormattingReason: formatting.Reason,
            SourceDisplayMode: sourceDisplay.Mode,
            SourceDisplayReason: sourceDisplay.Reason);
    }

    private static string ResolveTemplateFamily(AskWorkflowContext context)
    {
        var template = context.TemplateUsed ?? string.Empty;
        if (string.Equals(template, "risk_summary", StringComparison.OrdinalIgnoreCase))
        {
            return "risk";
        }

        if (string.Equals(template, "daily_briefing", StringComparison.OrdinalIgnoreCase))
        {
            return "daily_briefing";
        }

        if (string.Equals(template, "change_summary", StringComparison.OrdinalIgnoreCase))
        {
            return "what_changed";
        }

        if (string.Equals(template, "plan_summary", StringComparison.OrdinalIgnoreCase))
        {
            return "plan";
        }

        if (string.Equals(template, "code_answer", StringComparison.OrdinalIgnoreCase))
        {
            return "code";
        }

        if (string.Equals(template, "summary_answer", StringComparison.OrdinalIgnoreCase))
        {
            return "summary";
        }

        if (string.Equals(context.IntentType, "message_draft", StringComparison.OrdinalIgnoreCase))
        {
            return "message_draft";
        }

        if (string.Equals(context.IntentType, "practical_guidance", StringComparison.OrdinalIgnoreCase))
        {
            return "practical_guidance";
        }

        if (string.Equals(template, "clarification", StringComparison.OrdinalIgnoreCase))
        {
            return "clarification";
        }

        if (string.Equals(template, "low_confidence_escalation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.McpPolicyDecision, "blocked", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.McpPolicyDecision, "approval_required", StringComparison.OrdinalIgnoreCase))
        {
            return "blocked_action";
        }

        if (template.StartsWith("memory_", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "memory_recall", StringComparison.OrdinalIgnoreCase))
        {
            return "memory_backed";
        }

        if (HasSourceContext(context))
        {
            return "source_backed";
        }

        if (string.Equals(template, "system_truth_answer", StringComparison.OrdinalIgnoreCase))
        {
            return "system_truth";
        }

        if (string.Equals(template, "direct_answer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "deterministic_reasoning", StringComparison.OrdinalIgnoreCase))
        {
            return "direct_answer";
        }

        return "default_helpful";
    }

    private static RhythmDecision ResolveRhythm(
        AskWorkflowContext context,
        string templateFamily,
        string answer)
    {
        if (templateFamily is "risk" or "daily_briefing" or "what_changed" or "plan" or "blocked_action")
        {
            return new RhythmDecision("rhythm_structured", "rhythm_response_mode_forced");
        }

        if (templateFamily == "code")
        {
            return new RhythmDecision("rhythm_stepwise", "rhythm_code_blocks_preserved");
        }

        if (templateFamily == "message_draft")
        {
            return new RhythmDecision("rhythm_compact", "rhythm_message_draft_first");
        }

        if (templateFamily == "practical_guidance")
        {
            return new RhythmDecision("rhythm_compact", "rhythm_practical_next_action_first");
        }

        if (HasSourceContext(context))
        {
            return new RhythmDecision("rhythm_standard", "rhythm_source_backed_spacing");
        }

        if (string.Equals(context.VerbosityMode, "concise", StringComparison.OrdinalIgnoreCase)
            || answer.Length <= 320)
        {
            return new RhythmDecision("rhythm_compact", "rhythm_user_or_context_requested_brief");
        }

        return new RhythmDecision("rhythm_standard", "rhythm_readable_default");
    }

    private static FormattingDecision ResolveFormatting(
        AskWorkflowContext context,
        string templateFamily,
        string answer)
    {
        if (templateFamily == "code" || answer.Contains("```", StringComparison.Ordinal))
        {
            return new FormattingDecision("format_code_preserve", "format_code_blocks_must_remain_runnable");
        }

        if (context.MemoryConflictSummaries.Count > 0)
        {
            return new FormattingDecision("format_conflict_visible", "format_memory_conflict_must_not_be_hidden");
        }

        if (templateFamily is "risk" or "daily_briefing" or "what_changed" or "plan" or "blocked_action")
        {
            return new FormattingDecision("format_structured_sections", "format_response_mode_contract");
        }

        if (templateFamily == "message_draft")
        {
            return new FormattingDecision("format_message_first", "format_sendable_text_before_notes");
        }

        if (templateFamily == "practical_guidance")
        {
            return new FormattingDecision("format_compact", "format_practical_next_action_first");
        }

        if (HasSourceContext(context))
        {
            return new FormattingDecision("format_source_grouped", "format_sources_grouped_by_type");
        }

        if (string.Equals(context.VerbosityMode, "concise", StringComparison.OrdinalIgnoreCase))
        {
            return new FormattingDecision("format_compact", "format_concise_context");
        }

        return new FormattingDecision("format_default", "format_no_change_needed");
    }

    private static SourceDisplayDecision ResolveSourceDisplay(AskWorkflowContext context)
    {
        if (!HasSourceContext(context))
        {
            return new SourceDisplayDecision("source_display_none", "source_display_no_sources_used");
        }

        var sourceTypes = new List<string>();
        if (context.TrustedWebResults.Count > 0)
        {
            sourceTypes.Add("web");
        }

        if (context.MemoryResults.Count > 0 || context.MemoryConflictSummaries.Count > 0)
        {
            sourceTypes.Add("memory");
        }

        if (context.SourceResults.Count > 0 || context.Retrieved.Count > 0)
        {
            sourceTypes.Add("documents");
        }

        if (context.GraphEvidence.Count > 0)
        {
            sourceTypes.Add("graph");
        }

        if (context.McpResult is not null)
        {
            sourceTypes.Add("tools");
        }

        return new SourceDisplayDecision(
            "source_pills_grouped",
            sourceTypes.Count == 0
                ? "source_display_trace_only"
                : $"source_display_grouped_{string.Join("_", sourceTypes.Distinct(StringComparer.OrdinalIgnoreCase))}");
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

    private sealed record RhythmDecision(string Mode, string Reason);
    private sealed record FormattingDecision(string Mode, string Reason);
    private sealed record SourceDisplayDecision(string Mode, string Reason);
}

public sealed record PresentationDecision(
    string FrameworkVersion,
    string TemplateFamily,
    string TemplateReason,
    string RhythmMode,
    string RhythmReason,
    string FormattingMode,
    string FormattingReason,
    string SourceDisplayMode,
    string SourceDisplayReason);
#nullable disable
