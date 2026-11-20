#nullable enable
using Cwi.Orchestrator.Application.Workflows;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class ResponseIntelligenceOverlayService
{
    private readonly ComprehensionExpressionFrameworkService _comprehensionExpressionFramework;
    private readonly PresentationFrameworkService _presentationFramework;
    private readonly ChatResponseQualityService _chatResponseQuality;

    public ResponseIntelligenceOverlayService(
        ComprehensionExpressionFrameworkService? comprehensionExpressionFramework = null,
        PresentationFrameworkService? presentationFramework = null,
        ChatResponseQualityService? chatResponseQuality = null)
    {
        _comprehensionExpressionFramework = comprehensionExpressionFramework ?? new ComprehensionExpressionFrameworkService();
        _presentationFramework = presentationFramework ?? new PresentationFrameworkService();
        _chatResponseQuality = chatResponseQuality ?? new ChatResponseQualityService();
    }

    public void Apply(AskWorkflowContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ApplyComprehensionExpression(context);
        ApplyPresentation(context);
        ApplyChatResponseQuality(context);
    }

    private void ApplyComprehensionExpression(AskWorkflowContext context)
    {
        var decision = _comprehensionExpressionFramework.Evaluate(context);

        context.ComprehensionExpressionVersion = decision.FrameworkVersion;
        context.ContextAwarenessMode = decision.ContextMode;
        context.ClarityDecision = decision.ClarityDecision;
        context.ClarityReason = decision.ClarityReason;
        context.ExpressionToneProfile = decision.ToneProfile;
        context.ExpressionToneReason = decision.ToneReason;

        if (!string.IsNullOrWhiteSpace(decision.AudienceMode))
        {
            context.AudienceMode ??= decision.AudienceMode;
        }

        if (!string.IsNullOrWhiteSpace(decision.VerbosityMode))
        {
            context.VerbosityMode ??= decision.VerbosityMode;
        }

        context.ToneMode ??= decision.ToneProfile;
        if (string.Equals(decision.ToneReason, "tone_safety_override", StringComparison.OrdinalIgnoreCase))
        {
            context.ToneMode = decision.ToneProfile;
        }
    }

    private void ApplyPresentation(AskWorkflowContext context)
    {
        var decision = _presentationFramework.Evaluate(context, context.Answer ?? string.Empty);

        context.PresentationFrameworkVersion = decision.FrameworkVersion;
        context.PresentationTemplateFamily = decision.TemplateFamily;
        context.PresentationTemplateReason = decision.TemplateReason;
        context.PresentationRhythmMode = decision.RhythmMode;
        context.PresentationRhythmReason = decision.RhythmReason;
        context.PresentationFormattingMode = decision.FormattingMode;
        context.PresentationFormattingReason = decision.FormattingReason;
        context.PresentationSourceDisplayMode = decision.SourceDisplayMode;
        context.PresentationSourceDisplayReason = decision.SourceDisplayReason;
    }

    private void ApplyChatResponseQuality(AskWorkflowContext context)
    {
        var decision = _chatResponseQuality.Apply(context);

        context.ChatResponseQualityVersion = decision.FrameworkVersion;
        context.ChatResponseProfile = decision.Profile;
        context.ChatResponseProfileReason = decision.ProfileReason;
        context.ChatContextBudgetMode = decision.ContextBudgetMode;
        context.ChatContextBudgetReason = decision.ContextBudgetReason;
        context.ChatFinalPolishApplied = decision.FinalPolishApplied;
    }
}
#nullable disable

