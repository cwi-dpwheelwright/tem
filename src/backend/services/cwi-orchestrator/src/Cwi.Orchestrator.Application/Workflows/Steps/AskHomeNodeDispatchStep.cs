using Cwi.Common;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.HomeNodes;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Trace;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class AskHomeNodeDispatchStep : IAskWorkflowStep
{
    private readonly HomeNodeDispatchService _dispatchService;
    private readonly JourneyRequestContext _journeyRequestContext;
    private readonly TraceLookupStore _traceStore;
    private readonly ResponseIntelligenceOverlayService _responseIntelligenceOverlay;

    public AskHomeNodeDispatchStep(
        HomeNodeDispatchService dispatchService,
        JourneyRequestContext journeyRequestContext,
        TraceLookupStore traceStore,
        ResponseIntelligenceOverlayService? responseIntelligenceOverlay = null)
    {
        _dispatchService = dispatchService;
        _journeyRequestContext = journeyRequestContext;
        _traceStore = traceStore;
        _responseIntelligenceOverlay = responseIntelligenceOverlay ?? new ResponseIntelligenceOverlayService();
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        var dispatchDecision = await _dispatchService.TryDispatchAskAsync(context, cancellationToken).ConfigureAwait(false);
        if (!dispatchDecision.IsSuccess || dispatchDecision.Data is null)
        {
            return ServiceResponse<AskResponseDto>.Fail(
                dispatchDecision.Error!,
                statusCode: dispatchDecision.StatusCode,
                message: dispatchDecision.Message);
        }

        if (dispatchDecision.Data.ContinueSharedPath)
        {
            return await next().ConfigureAwait(false);
        }

        _responseIntelligenceOverlay.Apply(context);
        var response = AskResponseFactory.Create(context, _journeyRequestContext, _traceStore);
        return ServiceResponse<AskResponseDto>.Success(response);
    }
}

