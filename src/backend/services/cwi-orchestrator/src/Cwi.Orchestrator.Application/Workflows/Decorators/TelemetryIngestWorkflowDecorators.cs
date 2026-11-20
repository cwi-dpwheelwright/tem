#nullable enable
using Cwi.Common;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Workflows.Decorators;

public sealed class TelemetryIngestIdempotencyDecorator : ITelemetryIngestWorkflowDecorator
{
    public Task<ServiceResponse<TelemetryIngestResponseDto>> ExecuteAsync(
        TelemetryIngestWorkflowContext context,
        WorkflowStepDelegate<TelemetryIngestResponseDto> next,
        CancellationToken cancellationToken)
    {
        return next();
    }
}

public sealed class TelemetryIngestKillSwitchDecorator : ITelemetryIngestWorkflowDecorator
{
    public Task<ServiceResponse<TelemetryIngestResponseDto>> ExecuteAsync(
        TelemetryIngestWorkflowContext context,
        WorkflowStepDelegate<TelemetryIngestResponseDto> next,
        CancellationToken cancellationToken)
    {
        return next();
    }
}

public sealed class TelemetryIngestProvenanceDecorator : ITelemetryIngestWorkflowDecorator
{
    public Task<ServiceResponse<TelemetryIngestResponseDto>> ExecuteAsync(
        TelemetryIngestWorkflowContext context,
        WorkflowStepDelegate<TelemetryIngestResponseDto> next,
        CancellationToken cancellationToken)
    {
        return next();
    }
}

public sealed class TelemetryIngestMetricsDecorator : ITelemetryIngestWorkflowDecorator
{
    public Task<ServiceResponse<TelemetryIngestResponseDto>> ExecuteAsync(
        TelemetryIngestWorkflowContext context,
        WorkflowStepDelegate<TelemetryIngestResponseDto> next,
        CancellationToken cancellationToken)
    {
        return next();
    }
}
#nullable disable
