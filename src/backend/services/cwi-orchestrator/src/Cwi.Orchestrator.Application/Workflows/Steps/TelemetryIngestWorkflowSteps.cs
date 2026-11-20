#nullable enable
using System.Text.Json;
using Cwi.Common;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Telemetry;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class TelemetryIngestAcceptStep : ITelemetryIngestWorkflowStep
{
    public Task<ServiceResponse<TelemetryIngestResponseDto>> ExecuteAsync(
        TelemetryIngestWorkflowContext context,
        WorkflowStepDelegate<TelemetryIngestResponseDto> next,
        CancellationToken cancellationToken)
    {
        context.Accepted = true;
        context.IngestId = Guid.NewGuid().ToString("N");
        return next();
    }
}

public sealed class TelemetryIngestPersistStep : ITelemetryIngestWorkflowStep
{
    private readonly ITelemetryWindowRepository _repository;
    private readonly TelemetryIndexStore _indexStore;

    public TelemetryIngestPersistStep(
        ITelemetryWindowRepository repository,
        TelemetryIndexStore indexStore)
    {
        _repository = repository;
        _indexStore = indexStore;
    }

    public async Task<ServiceResponse<TelemetryIngestResponseDto>> ExecuteAsync(
        TelemetryIngestWorkflowContext context,
        WorkflowStepDelegate<TelemetryIngestResponseDto> next,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            signals = context.Signals,
            metadata = context.Metadata
        };

        var window = new TelemetryWindow
        {
            Id = Guid.NewGuid(),
            TenantId = context.TenantId,
            WindowStartUtc = context.Window.Start,
            WindowEndUtc = context.Window.End,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedUtc = DateTimeOffset.UtcNow
        };

        await _repository.AddAsync(window, cancellationToken).ConfigureAwait(false);
        _indexStore.Record(new TelemetryIndexEntry
        {
            IngestId = context.IngestId ?? string.Empty,
            TenantId = context.TenantId,
            WindowStartUtc = window.WindowStartUtc,
            WindowEndUtc = window.WindowEndUtc,
            CreatedUtc = window.CreatedUtc
        });
        return await next().ConfigureAwait(false);
    }
}

public sealed class TelemetryIngestResponseStep : ITelemetryIngestWorkflowStep
{
    public Task<ServiceResponse<TelemetryIngestResponseDto>> ExecuteAsync(
        TelemetryIngestWorkflowContext context,
        WorkflowStepDelegate<TelemetryIngestResponseDto> next,
        CancellationToken cancellationToken)
    {
        context.TraceId = Guid.NewGuid().ToString("N");

        var response = new TelemetryIngestResponseDto
        {
            Accepted = context.Accepted,
            IngestId = context.IngestId ?? string.Empty,
            Trace = new TracePayload
            {
                TraceId = context.TraceId,
                Sources = new List<string>(),
                Confidence = 0.0f,
                PolicyMode = string.Empty,
                Model = string.Empty,
                PackHash = string.Empty
            }
        };

        return Task.FromResult(ServiceResponse<TelemetryIngestResponseDto>.Success(response));
    }
}
#nullable disable
