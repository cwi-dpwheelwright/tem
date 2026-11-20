using System.Text.Json;
using Cwi.Common.Journey;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Journey;

public interface IJourneyService
{
    Task<IReadOnlyList<JourneyEvent>> ListByRequestIdAsync(string requestId, CancellationToken cancellationToken);
}

public sealed class JourneyService : IJourneyService
{
    private readonly IJourneyEventRepository _repository;

    public JourneyService(IJourneyEventRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<JourneyEvent>> ListByRequestIdAsync(string requestId, CancellationToken cancellationToken)
    {
        var records = await _repository.ListByRequestIdAsync(requestId, cancellationToken).ConfigureAwait(false);
        return records.Select(Map).ToList();
    }

    public static JourneyEvent Map(JourneyEventRecord record)
    {
        return new JourneyEvent
        {
            EventId = record.EventId,
            RequestId = record.RequestId,
            TraceId = record.TraceId,
            Service = record.Service,
            Component = record.Component,
            Phase = record.Phase,
            Direction = record.Direction,
            FromComponent = record.FromComponent,
            ToComponent = record.ToComponent,
            TenantId = record.TenantId,
            UserId = record.UserId,
            PackName = record.PackName,
            Method = record.Method,
            Path = record.Path,
            StatusCode = record.StatusCode,
            DurationMs = record.DurationMs,
            Message = record.Message,
            CreatedUtc = record.CreatedUtc,
            Metadata = ParseMetadata(record.MetadataJson)
        };
    }

    private static Dictionary<string, object?> ParseMetadata(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(metadataJson) ??
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
