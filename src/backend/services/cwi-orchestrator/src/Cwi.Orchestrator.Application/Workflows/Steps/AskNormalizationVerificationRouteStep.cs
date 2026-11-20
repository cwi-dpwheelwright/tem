#nullable enable
using Cwi.Common;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Trace;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class AskNormalizationVerificationRouteStep : IAskWorkflowStep
{
    private readonly JourneyRequestContext _journeyRequestContext;
    private readonly TraceLookupStore _traceStore;
    private readonly ResponseIntelligenceOverlayService _responseIntelligenceOverlay;

    public AskNormalizationVerificationRouteStep(
        JourneyRequestContext journeyRequestContext,
        TraceLookupStore traceStore,
        ResponseIntelligenceOverlayService? responseIntelligenceOverlay = null)
    {
        _journeyRequestContext = journeyRequestContext;
        _traceStore = traceStore;
        _responseIntelligenceOverlay = responseIntelligenceOverlay ?? new ResponseIntelligenceOverlayService();
    }

    public Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (!context.NormalizationFailed && !context.NormalizationRequiresVerification)
        {
            return next();
        }

        ApplyClarification(context);
        _responseIntelligenceOverlay.Apply(context);
        var response = AskResponseFactory.Create(context, _journeyRequestContext, _traceStore);
        return Task.FromResult(ServiceResponse<AskResponseDto>.Success(response));
    }

    private static void ApplyClarification(AskWorkflowContext context)
    {
        context.Answer = BuildVerificationAnswer(context);
        context.Route = "clarification_handler";
        context.EntityFound = false;
        context.QueryClassification = "clarification_required";
        context.QueryClassificationReason = context.NormalizationFailed
            ? "Language normalization failed; asking the user to rephrase before routing."
            : "Language normalization confidence was below the continuation threshold; asking the user to verify before routing.";
        context.IntentType = "needs_clarification";
        context.TemplateUsed = "clarification";
        context.ModelName = "normalization-verification";
        context.SkipModelInference = true;
        context.ExecutionTarget = "deterministic_runtime";
        context.GroundingMode = "non_grounded";
        context.RetrievalAllowed = false;
        context.RetrievalSkippedReason = context.NormalizationFailed
            ? "normalization_failed_requires_clarification"
            : "normalization_requires_verification";
        context.EvidenceRequired = false;
        context.Retrieved.Clear();
        context.GraphEvidence.Clear();
        context.MemoryResults = Array.Empty<GovernedMemoryResult>();
        context.SourceResults = Array.Empty<GovernedSourceResult>();
    }

    private static string BuildVerificationAnswer(AskWorkflowContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.NormalizationVerificationMessage))
        {
            return context.NormalizationVerificationMessage;
        }

        if (context.NormalizationFailed)
        {
            return "I did not catch that clearly. Can you rephrase the question?";
        }

        var candidates = context.NormalizationVerificationCandidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .ToArray();
        return candidates.Length switch
        {
            >= 2 => $"Did you mean \"{candidates[0]}\" or \"{candidates[1]}\"?",
            1 => $"Did you mean \"{candidates[0]}\"?",
            _ => "I may have misunderstood the wording. Can you confirm the question you want me to answer?"
        };
    }
}
#nullable disable
