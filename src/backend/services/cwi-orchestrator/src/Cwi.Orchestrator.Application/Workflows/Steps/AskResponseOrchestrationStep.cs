#nullable enable
using Cwi.Common;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Orchestrator.Application.Model;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Packs;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class AskResponseOrchestrationStep : IAskWorkflowStep
{
    private readonly JourneyEmitter _journeyEmitter;
    private readonly PresentationFrameworkService _presentationFramework;

    public AskResponseOrchestrationStep(
        IModelClient _,
        JourneyEmitter journeyEmitter,
        PresentationFrameworkService? presentationFramework = null)
    {
        _journeyEmitter = journeyEmitter;
        _presentationFramework = presentationFramework ?? new PresentationFrameworkService();
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Answer))
        {
            return await next().ConfigureAwait(false);
        }

        var responseOrchestrator = context.BaselinePackGuidance.FirstOrDefault(pack =>
            string.Equals(pack.PackName, "cwi-response-orchestrator", StringComparison.OrdinalIgnoreCase));
        if (responseOrchestrator is null && string.IsNullOrWhiteSpace(context.TemplateUsed))
        {
            ApplyPresentationDecision(context);
            return await next().ConfigureAwait(false);
        }

        if (responseOrchestrator is null)
        {
            context.Answer = GovernedAnswerPostProcessor.ApplyResponseOrchestration(context.Answer, context);
            ApplyPresentationDecision(context);
            return await next().ConfigureAwait(false);
        }

        var started = DateTimeOffset.UtcNow;
        await _journeyEmitter.EmitAsync(
            "response_orchestrator",
            "started",
            fromComponent: "cwi-orchestrator",
            toComponent: "cwi-orchestrator",
            metadata: new Dictionary<string, object?>
            {
                ["pack_id"] = responseOrchestrator.PackName,
                ["pack_version"] = responseOrchestrator.PackVersion,
                ["execution_mode"] = "inline_prompt_guidance"
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        context.Answer = GovernedAnswerPostProcessor.ApplyResponseOrchestration(context.Answer, context);
        ApplyPresentationDecision(context);

        await _journeyEmitter.EmitAsync(
            "response_orchestrator",
            "completed",
            direction: "response",
            fromComponent: "cwi-orchestrator",
            toComponent: "cwi-orchestrator",
            durationMs: (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            message: "Applied response orchestration guidance inline.",
            metadata: new Dictionary<string, object?>
            {
                ["pack_id"] = responseOrchestrator.PackName,
                ["pack_version"] = responseOrchestrator.PackVersion,
                ["execution_mode"] = "inline_prompt_guidance",
                ["template_used"] = context.TemplateUsed,
                ["tone_mode"] = context.ToneMode,
                ["presentation_template_family"] = context.PresentationTemplateFamily,
                ["presentation_rhythm_mode"] = context.PresentationRhythmMode,
                ["presentation_formatting_mode"] = context.PresentationFormattingMode,
                ["presentation_source_display_mode"] = context.PresentationSourceDisplayMode
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return await next().ConfigureAwait(false);
    }

    private void ApplyPresentationDecision(AskWorkflowContext context)
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
}
#nullable disable
