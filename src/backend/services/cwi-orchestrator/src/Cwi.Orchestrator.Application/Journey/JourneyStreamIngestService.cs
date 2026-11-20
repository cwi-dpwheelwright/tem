using System.Text.Json;
using Cwi.Common.Events;
using Cwi.Common.Journey;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Journey;

public sealed class JourneyStreamIngestService
{
    private readonly IJourneyEventRepository _repository;

    public JourneyStreamIngestService(IJourneyEventRepository repository)
    {
        _repository = repository;
    }

    public Task<string?> GetStartingStreamIdAsync(CancellationToken cancellationToken)
    {
        return _repository.GetLatestStreamIdAsync(cancellationToken);
    }

    public async Task<bool> PersistIfNewAsync(
        string streamId,
        JourneyEvent journeyEvent,
        CancellationToken cancellationToken)
    {
        if (await _repository.ExistsStreamIdAsync(streamId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var record = new JourneyEventRecord
        {
            EventId = journeyEvent.EventId,
            StreamId = streamId,
            RequestId = journeyEvent.RequestId,
            TraceId = journeyEvent.TraceId,
            Service = journeyEvent.Service,
            Component = journeyEvent.Component,
            Phase = journeyEvent.Phase,
            Direction = journeyEvent.Direction,
            FromComponent = journeyEvent.FromComponent,
            ToComponent = journeyEvent.ToComponent,
            TenantId = journeyEvent.TenantId,
            UserId = journeyEvent.UserId,
            PackName = journeyEvent.PackName,
            Method = journeyEvent.Method,
            Path = journeyEvent.Path,
            StatusCode = journeyEvent.StatusCode,
            DurationMs = journeyEvent.DurationMs,
            Message = journeyEvent.Message,
            MetadataJson = JsonSerializer.Serialize(journeyEvent.Metadata),
            CreatedUtc = journeyEvent.CreatedUtc
        };

        await _repository.AddAsync(record, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
