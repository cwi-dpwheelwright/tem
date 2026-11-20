#nullable enable
using Cwi.Orchestrator.Application.Workflows;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

internal static class GovernedResponseModes
{
    public const string RiskIntent = "risk_review";
    public const string RiskAnalysisIntent = "risk_analysis";
    public const string RiskTemplate = "risk_summary";
    public const string RiskTone = "risk_concise";

    public const string DailyBriefingIntent = "daily_briefing";
    public const string DailyBriefingTemplate = "daily_briefing";
    public const string DailyBriefingTone = "briefing_concise";

    public const string ChangeReviewIntent = "change_review";
    public const string ChangeReviewTemplate = "change_summary";
    public const string ChangeReviewTone = "change_concise";

    public const string PlanRequestIntent = "plan_request";
    public const string PlanningIntent = "planning";
    public const string PlanTemplate = "plan_summary";
    public const string PlanTone = "plan_concise";

    public static bool TryResolveStructuredMode(
        AskWorkflowContext context,
        out string template,
        out string tone)
    {
        if (IsRiskMode(context))
        {
            template = RiskTemplate;
            tone = RiskTone;
            return true;
        }

        if (IsDailyBriefingMode(context))
        {
            template = DailyBriefingTemplate;
            tone = DailyBriefingTone;
            return true;
        }

        if (IsChangeReviewMode(context))
        {
            template = ChangeReviewTemplate;
            tone = ChangeReviewTone;
            return true;
        }

        if (IsPlanMode(context))
        {
            template = PlanTemplate;
            tone = PlanTone;
            return true;
        }

        template = string.Empty;
        tone = string.Empty;
        return false;
    }

    public static bool IsRiskMode(AskWorkflowContext context)
    {
        return string.Equals(context.IntentType, RiskIntent, StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.IntentType, RiskAnalysisIntent, StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.TemplateUsed, RiskTemplate, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDailyBriefingMode(AskWorkflowContext context)
    {
        return string.Equals(context.IntentType, DailyBriefingIntent, StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.TemplateUsed, DailyBriefingTemplate, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsChangeReviewMode(AskWorkflowContext context)
    {
        return string.Equals(context.IntentType, ChangeReviewIntent, StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.TemplateUsed, ChangeReviewTemplate, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPlanMode(AskWorkflowContext context)
    {
        return string.Equals(context.IntentType, PlanRequestIntent, StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.IntentType, PlanningIntent, StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.TemplateUsed, PlanTemplate, StringComparison.OrdinalIgnoreCase);
    }
}
#nullable disable
