#nullable enable
using Cwi.Common;
using System.Linq;
using Cwi.Orchestrator.Application.Inference;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Telemetry;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class AskDiscrepancyStep : IAskWorkflowStep
{
    private readonly DiscrepancyEvaluator _evaluator;
    private readonly InferenceLedgerRecorder _ledgerRecorder;
    private readonly ICorrectionGenerator _correctionGenerator;
    private readonly IFallbackSelector _fallbackSelector;
    private readonly DiscrepancyApprovalService _approvalService;
    private readonly OrchestratorMetrics _metrics;

    public AskDiscrepancyStep(
        DiscrepancyEvaluator evaluator,
        InferenceLedgerRecorder ledgerRecorder,
        ICorrectionGenerator correctionGenerator,
        IFallbackSelector fallbackSelector,
        DiscrepancyApprovalService approvalService,
        OrchestratorMetrics metrics)
    {
        _evaluator = evaluator;
        _ledgerRecorder = ledgerRecorder;
        _correctionGenerator = correctionGenerator;
        _fallbackSelector = fallbackSelector;
        _approvalService = approvalService;
        _metrics = metrics;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.TraceId))
        {
            context.TraceId = Guid.NewGuid().ToString("N");
        }

        var evaluation = _evaluator.Evaluate(context.Answer, context.Retrieved);
        if (evaluation.Discrepancies.Count > 0)
        {
            var correctionPlan = _correctionGenerator.Generate(context.Answer, evaluation);
            var fallback = _fallbackSelector.Select(
                context.Answer,
                evaluation,
                correctionPlan,
                context.PolicyMode,
                context.SurvivalMode,
                context.SurvivalOfflineMessage);

            context.Discrepancies = evaluation.Discrepancies;
            context.Corrections = correctionPlan.Corrections;
            context.DiscrepancySeverity = evaluation.Severity;
            context.DiscrepancyReason = evaluation.Reason;
            context.DiscrepancyScore = evaluation.Score;
            _metrics.RecordDiscrepancy(evaluation.Severity);

            if (fallback.Applied && !string.IsNullOrWhiteSpace(fallback.Answer))
            {
                var original = context.Answer ?? string.Empty;
                context.Answer = fallback.Answer;
                var corrections = context.Corrections.ToList();
                corrections.Add(new CorrectionDiff
                {
                    Field = "answer",
                    From = original,
                    To = fallback.Answer,
                    Note = fallback.Reason ?? "Fallback response issued due to discrepancy."
                });
                context.Corrections = corrections;
                context.FallbackApplied = true;
            }
        }

        if (context.TraceId is not null && evaluation.Alignment is not null)
        {
            await _ledgerRecorder.RecordAlignmentAsync(context.TraceId, evaluation.Alignment, cancellationToken)
                .ConfigureAwait(false);
        }

        if (context.TraceId is not null && evaluation.Discrepancies.Count > 0)
        {
            await _ledgerRecorder.RecordDiscrepanciesAsync(
                    context.TraceId,
                    evaluation.Severity,
                    evaluation.Score,
                    evaluation.Discrepancies,
                    context.Corrections,
                    context.FallbackApplied,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (context.TraceId is not null && evaluation.Discrepancies.Count > 0)
        {
            var approval = await _approvalService
                .CreateIfRequiredAsync(
                    context.TenantId,
                    context.UserId,
                    context.TraceId,
                    evaluation,
                    cancellationToken)
                .ConfigureAwait(false);

            if (approval is not null)
            {
                context.DiscrepancyRequiresApproval = true;
                context.DiscrepancyTicketId = approval.TicketId;
            }
        }

        return await next().ConfigureAwait(false);
    }
}
#nullable disable
