#nullable enable
using Cwi.Common;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Runtime;
using Cwi.Orchestrator.Application.Journey;
using Microsoft.Extensions.Logging;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class AskTruthEngineStep : IAskWorkflowStep
{
    private readonly TruthEngineService _truthEngine;
    private readonly TruthEngineLedgerRecorder _ledgerRecorder;
    private readonly SelectiveEnforcementOptions _options;
    private readonly JourneyEmitter _journeyEmitter;
    private readonly ILogger<AskTruthEngineStep> _logger;

    public AskTruthEngineStep(
        TruthEngineService truthEngine,
        TruthEngineLedgerRecorder ledgerRecorder,
        SelectiveEnforcementOptions options,
        JourneyEmitter journeyEmitter,
        ILogger<AskTruthEngineStep> logger)
    {
        _truthEngine = truthEngine;
        _ledgerRecorder = ledgerRecorder;
        _options = options;
        _journeyEmitter = journeyEmitter;
        _logger = logger;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (!_options.IsEnabled(SelectiveEnforcementFeature.Truth))
        {
            return await next().ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(context.TraceId))
        {
            context.TraceId = Guid.NewGuid().ToString("N");
        }

        try
        {
            var mode = _options.Resolve(SelectiveEnforcementFeature.Truth);
            var decision = _truthEngine.Evaluate(context, mode);
            context.TruthEngineVersion = decision.Version;
            context.TruthEngineMode = decision.Mode;
            context.TruthState = decision.State;
            context.TruthReason = decision.Reason;
            context.TruthEvidenceCount = decision.EvidenceCount;
            context.TruthConflictCount = decision.ConflictCount;
            context.TruthEvidenceKinds = decision.EvidenceKinds;
            context.TruthEvaluatedUtc = decision.EvaluatedUtc;

            await _ledgerRecorder.RecordAsync(context, decision, cancellationToken).ConfigureAwait(false);

            await _journeyEmitter.EmitAsync(
                "truth_engine",
                "observed",
                fromComponent: "cwi-orchestrator",
                toComponent: "truth_engine",
                metadata: new Dictionary<string, object?>
                {
                    ["mode"] = decision.Mode,
                    ["truth_state"] = decision.State,
                    ["truth_reason"] = decision.Reason,
                    ["evidence_count"] = decision.EvidenceCount,
                    ["conflict_count"] = decision.ConflictCount
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Truth engine observe-only evaluation failed for trace {TraceId}.", context.TraceId);
        }

        return await next().ConfigureAwait(false);
    }
}
#nullable disable
