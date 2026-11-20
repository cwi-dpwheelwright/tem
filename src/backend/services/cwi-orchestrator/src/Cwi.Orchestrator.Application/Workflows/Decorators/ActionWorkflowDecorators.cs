#nullable enable
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Guardrails.Provenance;
using Cwi.Orchestrator.Application.Guardrails.Scoring;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Policy;
using Cwi.Orchestrator.Application.Survival.Provenance;

namespace Cwi.Orchestrator.Application.Workflows.Decorators;

public sealed class ActionIdempotencyDecorator : IActionWorkflowDecorator
{
    public Task<ServiceResponse<ActionResponseDto>> ExecuteAsync(
        ActionWorkflowContext context,
        WorkflowStepDelegate<ActionResponseDto> next,
        CancellationToken cancellationToken)
    {
        return next();
    }
}

public sealed class ActionKillSwitchDecorator : IActionWorkflowDecorator
{
    private readonly KillSwitchEvaluator _killSwitchEvaluator;

    public ActionKillSwitchDecorator(KillSwitchEvaluator killSwitchEvaluator)
    {
        _killSwitchEvaluator = killSwitchEvaluator;
    }

    public Task<ServiceResponse<ActionResponseDto>> ExecuteAsync(
        ActionWorkflowContext context,
        WorkflowStepDelegate<ActionResponseDto> next,
        CancellationToken cancellationToken)
    {
        return ExecuteInternalAsync(context, next, cancellationToken);
    }

    private async Task<ServiceResponse<ActionResponseDto>> ExecuteInternalAsync(
        ActionWorkflowContext context,
        WorkflowStepDelegate<ActionResponseDto> next,
        CancellationToken cancellationToken)
    {
        var killSwitch = await _killSwitchEvaluator
            .IsActionDisabledAsync(context.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (!killSwitch.IsSuccess)
        {
            return ServiceResponse<ActionResponseDto>.Fail(
                killSwitch.Error ?? new DomainError(ErrorCodes.InternalError, "Kill switch unavailable."),
                statusCode: killSwitch.StatusCode,
                message: killSwitch.Message);
        }

        if (killSwitch.Data)
        {
            return ServiceResponse<ActionResponseDto>.Fail(
                new DomainError(ErrorCodes.Forbidden, "Kill switch enabled."),
                statusCode: 403,
                message: "Action requests disabled");
        }

        return await next().ConfigureAwait(false);
    }
}

public sealed class ActionProvenanceDecorator : IActionWorkflowDecorator
{
    private readonly IGuardrailLedgerRecorder _ledgerRecorder;
    private readonly SeverityScorer _scorer;

    public ActionProvenanceDecorator(IGuardrailLedgerRecorder ledgerRecorder, SeverityScorer scorer)
    {
        _ledgerRecorder = ledgerRecorder;
        _scorer = scorer;
    }

    public Task<ServiceResponse<ActionResponseDto>> ExecuteAsync(
        ActionWorkflowContext context,
        WorkflowStepDelegate<ActionResponseDto> next,
        CancellationToken cancellationToken)
    {
        return ExecuteInternalAsync(context, next, cancellationToken);
    }

    private async Task<ServiceResponse<ActionResponseDto>> ExecuteInternalAsync(
        ActionWorkflowContext context,
        WorkflowStepDelegate<ActionResponseDto> next,
        CancellationToken cancellationToken)
    {
        var response = await next().ConfigureAwait(false);
        if (!response.IsSuccess || context.TraceId is null)
        {
            return response;
        }

        if (context.GuardrailViolations.Count == 0)
        {
            return response;
        }

        var score = _scorer.Score(context.GuardrailViolations);
        await _ledgerRecorder.RecordAsync(
            context.TraceId,
            flow: "action",
            policyMode: context.PolicyMode,
            actionType: context.ActionType,
            modelId: context.ModelName,
            packHash: context.PackHash,
            packVersion: context.PackVersion,
            policyVersion: context.PolicyVersion,
            violations: context.GuardrailViolations,
            score: score,
            decision: context.GuardrailDecision,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return response;
    }
}

public sealed class ActionSurvivalLedgerDecorator : IActionWorkflowDecorator
{
    private readonly SurvivalLedgerRecorder _ledgerRecorder;

    public ActionSurvivalLedgerDecorator(SurvivalLedgerRecorder ledgerRecorder)
    {
        _ledgerRecorder = ledgerRecorder;
    }

    public Task<ServiceResponse<ActionResponseDto>> ExecuteAsync(
        ActionWorkflowContext context,
        WorkflowStepDelegate<ActionResponseDto> next,
        CancellationToken cancellationToken)
    {
        return ExecuteInternalAsync(context, next, cancellationToken);
    }

    private async Task<ServiceResponse<ActionResponseDto>> ExecuteInternalAsync(
        ActionWorkflowContext context,
        WorkflowStepDelegate<ActionResponseDto> next,
        CancellationToken cancellationToken)
    {
        var response = await next().ConfigureAwait(false);
        if (!response.IsSuccess || context.TraceId is null)
        {
            return response;
        }

        await _ledgerRecorder
            .RecordTraceAsync(
                context.TraceId,
                context.SurvivalMode,
                context.SurvivalProfile,
                context.SurvivalLastSignalUtc,
                cancellationToken)
            .ConfigureAwait(false);

        return response;
    }
}

public sealed class ActionMetricsDecorator : IActionWorkflowDecorator
{
    public Task<ServiceResponse<ActionResponseDto>> ExecuteAsync(
        ActionWorkflowContext context,
        WorkflowStepDelegate<ActionResponseDto> next,
        CancellationToken cancellationToken)
    {
        return next();
    }
}
#nullable disable
