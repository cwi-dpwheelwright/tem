#nullable enable
using System.Text;
using Cwi.Common;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class AskProactiveIntelligenceStep : IAskWorkflowStep
{
    private readonly UserIntelligenceService _intelligenceService;
    private readonly JourneyEmitter _journeyEmitter;

    public AskProactiveIntelligenceStep(
        UserIntelligenceService intelligenceService,
        JourneyEmitter journeyEmitter)
    {
        _intelligenceService = intelligenceService;
        _journeyEmitter = journeyEmitter;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (!IsProactiveIntent(context.IntentType))
        {
            return await next().ConfigureAwait(false);
        }

        var started = DateTimeOffset.UtcNow;
        await _journeyEmitter.EmitAsync(
            "proactive_intelligence",
            "started",
            fromComponent: "cwi-orchestrator",
            toComponent: "user_intelligence_api",
            metadata: new Dictionary<string, object?>
            {
                ["intent_type"] = context.IntentType,
                ["top_k"] = context.TopK
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        ProactiveAnswer answer;
        try
        {
            answer = context.IntentType?.ToLowerInvariant() switch
            {
                GovernedResponseModes.DailyBriefingIntent => await BuildDailyBriefingAsync(context, cancellationToken).ConfigureAwait(false),
                GovernedResponseModes.RiskIntent => await BuildRiskReviewAsync(context, cancellationToken).ConfigureAwait(false),
                GovernedResponseModes.RiskAnalysisIntent => await BuildRiskReviewAsync(context, cancellationToken).ConfigureAwait(false),
                GovernedResponseModes.ChangeReviewIntent => await BuildChangeReviewAsync(context, cancellationToken).ConfigureAwait(false),
                GovernedResponseModes.PlanRequestIntent => await BuildPlanAsync(context, cancellationToken).ConfigureAwait(false),
                GovernedResponseModes.PlanningIntent => await BuildPlanAsync(context, cancellationToken).ConfigureAwait(false),
                _ => ProactiveAnswer.Unavailable("general_answer", "plain_direct")
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            answer = ProactiveAnswer.Unavailable(
                ResolveTemplate(context.IntentType),
                ResolveTone(context.IntentType));
            AddWarning(context, "proactive_intelligence_unavailable");
        }

        ApplyAnswer(context, answer);

        await _journeyEmitter.EmitAsync(
            "proactive_intelligence",
            answer.HasData ? "completed" : "completed_empty",
            direction: "response",
            fromComponent: "user_intelligence_api",
            toComponent: "cwi-orchestrator",
            durationMs: (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            metadata: new Dictionary<string, object?>
            {
                ["intent_type"] = context.IntentType,
                ["template_used"] = context.TemplateUsed,
                ["has_data"] = answer.HasData,
                ["api"] = answer.ApiName
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return await next().ConfigureAwait(false);
    }

    private async Task<ProactiveAnswer> BuildDailyBriefingAsync(
        AskWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var result = await _intelligenceService
            .GetBriefingAsync(context.TenantId, context.UserId, ClampTopK(context.TopK, 5), cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return ProactiveAnswer.Unavailable(GovernedResponseModes.DailyBriefingTemplate, GovernedResponseModes.DailyBriefingTone);
        }

        var briefing = result.Data;
        var priorities = TakeDistinct(
            briefing.Plan.Select(PlanText)
                .Concat(SummaryItems(briefing.DailySummary, "priorities", "open_loops", "current_work"))
                .Concat(briefing.Changes.Select(SignalText)),
            3);
        var risks = TakeDistinct(briefing.Risks.Select(SignalText), 1);
        var recommendation = TakeDistinct(briefing.Suggestions.Select(SuggestionText), 1);
        var hasData = priorities.Count > 0 || risks.Count > 0 || recommendation.Count > 0;

        var builder = new StringBuilder();
        AppendList(builder, "Priorities:", priorities, "I do not have current calendar, task, or reminder details to rank priorities yet.");
        AppendList(builder, "Risk:", risks, "No specific risk is visible from the information I can access right now.");
        AppendList(builder, "Recommendation:", recommendation, "Add or connect the relevant calendar, task, or note source, then ask for the briefing again.");

        return new ProactiveAnswer(
            Normalize(builder.ToString()),
            GovernedResponseModes.DailyBriefingTemplate,
            GovernedResponseModes.DailyBriefingTone,
            hasData,
            "user_intelligence.briefing");
    }

    private async Task<ProactiveAnswer> BuildRiskReviewAsync(
        AskWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var risks = await _intelligenceService
            .GetRisksAsync(context.TenantId, context.UserId, 2, cancellationToken)
            .ConfigureAwait(false);
        var suggestions = await _intelligenceService
            .GetSuggestionsAsync(context.TenantId, context.UserId, 3, cancellationToken)
            .ConfigureAwait(false);

        if (!risks.IsSuccess || risks.Data is null)
        {
            return ProactiveAnswer.Unavailable(GovernedResponseModes.RiskTemplate, GovernedResponseModes.RiskTone);
        }

        var riskItems = TakeDistinct(risks.Data.Risks.Select(SignalText), 2);
        var suggestionItems = TakeDistinct(suggestions.Data?.Suggestions.Select(SuggestionText) ?? Array.Empty<string>(), 3);
        var hasData = riskItems.Count > 0 || suggestionItems.Count > 0;

        var builder = new StringBuilder();
        AppendList(builder, "Risks:", riskItems, "No specific risk is visible from the information I can access right now.");
        builder.AppendLine();
        builder.AppendLine("Impact:");
        builder.AppendLine(riskItems.Count > 0
            ? "These items are most likely to affect timing, quality, access, cost, or user trust if they are not handled."
            : "Treat the risk picture as unknown until current task, calendar, note, or project details are available.");
        builder.AppendLine();
        builder.AppendLine("Next action:");
        builder.AppendLine(suggestionItems.FirstOrDefault() ?? "Add or connect the relevant task, calendar, note, or project source, then rerun the risk review.");
        if (suggestionItems.Count > 0)
        {
            AppendList(builder, "Suggestions:", suggestionItems, null);
        }

        return new ProactiveAnswer(
            Normalize(builder.ToString()),
            GovernedResponseModes.RiskTemplate,
            GovernedResponseModes.RiskTone,
            hasData,
            "user_intelligence.risk");
    }

    private async Task<ProactiveAnswer> BuildChangeReviewAsync(
        AskWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var changes = await _intelligenceService
            .GetChangesAsync(context.TenantId, context.UserId, ClampTopK(context.TopK, 5), cancellationToken)
            .ConfigureAwait(false);
        var risks = await _intelligenceService
            .GetRisksAsync(context.TenantId, context.UserId, 2, cancellationToken)
            .ConfigureAwait(false);
        var suggestions = await _intelligenceService
            .GetSuggestionsAsync(context.TenantId, context.UserId, 3, cancellationToken)
            .ConfigureAwait(false);

        if (!changes.IsSuccess || changes.Data is null)
        {
            return ProactiveAnswer.Unavailable(GovernedResponseModes.ChangeReviewTemplate, GovernedResponseModes.ChangeReviewTone);
        }

        var changeItems = TakeDistinct(changes.Data.Changes.Select(SignalText), 5);
        var riskItems = TakeDistinct(risks.Data?.Risks.Select(SignalText) ?? Array.Empty<string>(), 2);
        var suggestionItems = TakeDistinct(suggestions.Data?.Suggestions.Select(SuggestionText) ?? Array.Empty<string>(), 3);
        var hasData = changeItems.Count > 0 || riskItems.Count > 0 || suggestionItems.Count > 0;

        var builder = new StringBuilder();
        AppendList(builder, "Changes:", changeItems, "I do not have recent change details available.");
        AppendList(builder, "Risks:", riskItems, "No current risk was found from those changes.");
        AppendList(builder, "Suggestions:", suggestionItems, "Add or connect the relevant task, calendar, note, or project source if the change picture looks incomplete.");

        return new ProactiveAnswer(
            Normalize(builder.ToString()),
            GovernedResponseModes.ChangeReviewTemplate,
            GovernedResponseModes.ChangeReviewTone,
            hasData,
            "user_intelligence.changes");
    }

    private async Task<ProactiveAnswer> BuildPlanAsync(
        AskWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var plan = await _intelligenceService
            .GetPlanAsync(context.TenantId, context.UserId, ClampTopK(context.TopK, 5), cancellationToken)
            .ConfigureAwait(false);
        var risks = await _intelligenceService
            .GetRisksAsync(context.TenantId, context.UserId, 2, cancellationToken)
            .ConfigureAwait(false);
        var suggestions = await _intelligenceService
            .GetSuggestionsAsync(context.TenantId, context.UserId, 3, cancellationToken)
            .ConfigureAwait(false);

        if (!plan.IsSuccess || plan.Data is null)
        {
            return ProactiveAnswer.Unavailable(GovernedResponseModes.PlanTemplate, GovernedResponseModes.PlanTone);
        }

        var priorityItems = TakeDistinct(plan.Data.Items.Select(PlanText), 5);
        var riskItems = TakeDistinct(risks.Data?.Risks.Select(SignalText) ?? Array.Empty<string>(), 2);
        var suggestionItems = TakeDistinct(suggestions.Data?.Suggestions.Select(SuggestionText) ?? Array.Empty<string>(), 3);
        var hasData = priorityItems.Count > 0 || riskItems.Count > 0 || suggestionItems.Count > 0;

        var builder = new StringBuilder();
        AppendList(builder, "Priorities:", priorityItems, "I do not have current task, calendar, or project details to rank priorities yet.");
        AppendList(builder, "Risks:", riskItems, "No specific risk is visible from the information I can access right now.");
        AppendList(builder, "Suggestions:", suggestionItems, "Pick one known priority, confirm the next deadline, then ask again once current task or calendar details are available.");

        return new ProactiveAnswer(
            Normalize(builder.ToString()),
            GovernedResponseModes.PlanTemplate,
            GovernedResponseModes.PlanTone,
            hasData,
            "user_intelligence.plan");
    }

    private static void ApplyAnswer(AskWorkflowContext context, ProactiveAnswer answer)
    {
        context.Answer = answer.Answer;
        context.Route = "proactive_intelligence";
        context.QueryClassification = "proactive_intelligence_request";
        context.QueryClassificationReason ??= "Proactive command routed to user intelligence API.";
        context.TemplateUsed = answer.TemplateUsed;
        context.ToneMode = answer.ToneMode;
        context.VerbosityMode = "concise";
        context.ModelName = answer.ApiName;
        context.SkipModelInference = true;
        context.ExecutionTarget = "proactive_intelligence_api";
        context.RetrievalAllowed = false;
        context.RetrievalSkippedReason = "proactive_intelligence_api_route";
        context.EvidenceRequired = false;
        context.GroundingMode = "proactive_intelligence";
        context.Grounded = answer.HasData;
        context.ReasoningBased = false;
        context.Retrieved.Clear();
        context.GraphEvidence.Clear();
        context.MemoryResults = Array.Empty<GovernedMemoryResult>();
        context.SourceResults = Array.Empty<GovernedSourceResult>();
    }

    private static bool IsProactiveIntent(string? intentType)
    {
        return string.Equals(intentType, GovernedResponseModes.DailyBriefingIntent, StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, GovernedResponseModes.RiskIntent, StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, GovernedResponseModes.RiskAnalysisIntent, StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, GovernedResponseModes.ChangeReviewIntent, StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, GovernedResponseModes.PlanRequestIntent, StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, GovernedResponseModes.PlanningIntent, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveTemplate(string? intentType)
    {
        return intentType?.ToLowerInvariant() switch
        {
            GovernedResponseModes.DailyBriefingIntent => GovernedResponseModes.DailyBriefingTemplate,
            GovernedResponseModes.RiskIntent => GovernedResponseModes.RiskTemplate,
            GovernedResponseModes.RiskAnalysisIntent => GovernedResponseModes.RiskTemplate,
            GovernedResponseModes.ChangeReviewIntent => GovernedResponseModes.ChangeReviewTemplate,
            GovernedResponseModes.PlanRequestIntent => GovernedResponseModes.PlanTemplate,
            GovernedResponseModes.PlanningIntent => GovernedResponseModes.PlanTemplate,
            _ => "general_answer"
        };
    }

    private static string ResolveTone(string? intentType)
    {
        return intentType?.ToLowerInvariant() switch
        {
            GovernedResponseModes.DailyBriefingIntent => GovernedResponseModes.DailyBriefingTone,
            GovernedResponseModes.RiskIntent => GovernedResponseModes.RiskTone,
            GovernedResponseModes.RiskAnalysisIntent => GovernedResponseModes.RiskTone,
            GovernedResponseModes.ChangeReviewIntent => GovernedResponseModes.ChangeReviewTone,
            GovernedResponseModes.PlanRequestIntent => GovernedResponseModes.PlanTone,
            GovernedResponseModes.PlanningIntent => GovernedResponseModes.PlanTone,
            _ => "plain_direct"
        };
    }

    private static IReadOnlyList<string> SummaryItems(UserIntelligenceSummaryDto? summary, params string[] sectionNames)
    {
        if (summary is null)
        {
            return Array.Empty<string>();
        }

        var names = sectionNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return summary.Sections
            .Where(section => names.Contains(section.Name))
            .SelectMany(section => section.Items)
            .Select(item => item.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
    }

    private static string SignalText(UserIntelligenceSignalDto signal)
    {
        return FirstNonBlank(signal.Detail, signal.Title, signal.Category);
    }

    private static string PlanText(UserPlanItemDto item)
    {
        return FirstNonBlank(item.Title, item.Rationale, item.Category);
    }

    private static string SuggestionText(UserSuggestionDto suggestion)
    {
        if (!string.IsNullOrWhiteSpace(suggestion.Title)
            && !string.IsNullOrWhiteSpace(suggestion.Rationale)
            && !suggestion.Rationale.Contains(suggestion.Title, StringComparison.OrdinalIgnoreCase))
        {
            return $"{suggestion.Title}: {suggestion.Rationale}";
        }

        return FirstNonBlank(suggestion.Rationale, suggestion.Title, suggestion.Type);
    }

    private static IReadOnlyList<string> TakeDistinct(IEnumerable<string?> values, int max)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Compact(value!, 180))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, max))
            .ToArray();
    }

    private static void AppendList(
        StringBuilder builder,
        string title,
        IReadOnlyList<string> items,
        string? fallback)
    {
        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.AppendLine(title);
        var values = items.Count > 0
            ? items
            : string.IsNullOrWhiteSpace(fallback)
                ? Array.Empty<string>()
                : new[] { fallback };

        for (var i = 0; i < values.Count; i++)
        {
            builder.AppendLine($"{i + 1}. {TrimTrailingPeriod(values[i])}.");
        }
    }

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static int ClampTopK(int topK, int max)
    {
        return Math.Clamp(topK <= 0 ? max : topK, 1, max);
    }

    private static string Compact(string value, int maxChars)
    {
        var normalized = Normalize(value);
        return normalized.Length <= maxChars
            ? normalized
            : $"{normalized[..maxChars].TrimEnd()}...";
    }

    private static string TrimTrailingPeriod(string value)
    {
        return value.Trim().TrimEnd('.');
    }

    private static string Normalize(string value)
    {
        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private static void AddWarning(AskWorkflowContext context, string warning)
    {
        if (!context.ResponseWarnings.Contains(warning, StringComparer.OrdinalIgnoreCase))
        {
            context.ResponseWarnings.Add(warning);
        }
    }

    private sealed record ProactiveAnswer(
        string Answer,
        string TemplateUsed,
        string ToneMode,
        bool HasData,
        string ApiName)
    {
        public static ProactiveAnswer Unavailable(string templateUsed, string toneMode)
        {
            var answer = templateUsed switch
            {
                GovernedResponseModes.RiskTemplate => """
                    Risks:
                    1. I do not have enough current task, calendar, note, or project details to name a specific risk.

                    Impact:
                    Treat this as unknown, not clear.

                    Next action:
                    Add or connect the relevant task, calendar, note, or project source, then rerun the risk review.
                    """,
                GovernedResponseModes.DailyBriefingTemplate => """
                    Priorities:
                    1. I do not have current calendar, task, or reminder details to rank today's priorities.

                    Risk:
                    1. The main risk is missing or stale context.

                    Recommendation:
                    1. Add or connect the relevant calendar, task, or note source, then ask for the daily briefing again.
                    """,
                GovernedResponseModes.ChangeReviewTemplate => """
                    Changes:
                    1. I do not have recent change details available.

                    Risks:
                    1. The main risk is missing or stale context.

                    Suggestions:
                    1. Add or connect the relevant task, calendar, note, or project source, then ask for the change review again.
                    """,
                GovernedResponseModes.PlanTemplate => """
                    Priorities:
                    1. I do not have current task, calendar, or project details to build a reliable plan.

                    Risks:
                    1. The main risk is missing or stale context.

                    Suggestions:
                    1. Pick one known priority, confirm the next deadline, then ask again once current task or calendar details are available.
                    """,
                _ => "I do not have enough current task, calendar, note, or project details to answer that proactive request."
            };

            return new ProactiveAnswer(Normalize(answer), templateUsed, toneMode, HasData: false, "user_intelligence.unavailable");
        }
    }
}
#nullable disable
