#nullable enable
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Workflows;

public sealed class TelemetryIngestWorkflowContext
{
    public TelemetryIngestWorkflowContext(
        string tenantId,
        TelemetryWindowDto window,
        IReadOnlyList<TelemetrySignalDto> signals,
        IReadOnlyDictionary<string, object?> metadata)
    {
        TenantId = tenantId;
        Window = window;
        Signals = signals;
        Metadata = metadata;
    }

    public string TenantId { get; }
    public TelemetryWindowDto Window { get; }
    public IReadOnlyList<TelemetrySignalDto> Signals { get; }
    public IReadOnlyDictionary<string, object?> Metadata { get; }

    public bool Accepted { get; set; }
    public string? IngestId { get; set; }
    public string? TraceId { get; set; }
}

public interface ITelemetryIngestWorkflowStep : IWorkflowStep<TelemetryIngestWorkflowContext, TelemetryIngestResponseDto>
{
}

public interface ITelemetryIngestWorkflowDecorator : IWorkflowDecorator<TelemetryIngestWorkflowContext, TelemetryIngestResponseDto>
{
}

public sealed class TelemetryIngestWorkflowPipeline : WorkflowPipeline<TelemetryIngestWorkflowContext, TelemetryIngestResponseDto>
{
    public TelemetryIngestWorkflowPipeline(
        IEnumerable<ITelemetryIngestWorkflowStep> steps,
        IEnumerable<ITelemetryIngestWorkflowDecorator> decorators)
        : base(steps, decorators)
    {
    }
}
#nullable disable
