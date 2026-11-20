using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.Application.Services;

public sealed class MeshTopologyService
{
    private readonly IMeshTopologyRepository _repository;
    private readonly IMeshEventRepository _eventRepository;
    private readonly LinkStateThresholdOptions _thresholds;
    private readonly MeshEventQueryOptions _eventOptions;

    public MeshTopologyService(
        IMeshTopologyRepository repository,
        IMeshEventRepository eventRepository,
        LinkStateThresholdOptions thresholds,
        MeshEventQueryOptions eventOptions)
    {
        _repository = repository;
        _eventRepository = eventRepository;
        _thresholds = thresholds;
        _eventOptions = eventOptions;
    }

    public async Task<ServiceResponse<LinkStateResponse>> UpsertAsync(
        LinkStateUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.NodeId))
        {
            return ServiceResponse<LinkStateResponse>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "node_id is required."),
                statusCode: 400,
                message: "node_id required");
        }

        var interfaceKind = ParseInterface(request.Interface);
        var status = EvaluateStatus(request);

        var previous = await _repository.GetAsync(request.NodeId, interfaceKind, cancellationToken).ConfigureAwait(false);
        var state = new LinkState
        {
            NodeId = request.NodeId,
            Interface = interfaceKind,
            SignalQuality = request.SignalQuality,
            LatencyMs = request.LatencyMs,
            JitterMs = request.JitterMs,
            PacketLoss = request.PacketLoss,
            BandwidthKbps = request.BandwidthKbps,
            ThroughputKbps = request.ThroughputKbps,
            RssiDbm = request.RssiDbm,
            SnrDb = request.SnrDb,
            Status = status,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        await _repository.UpsertAsync(state, cancellationToken).ConfigureAwait(false);
        await RecordEventIfChangedAsync(previous, state, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<LinkStateResponse>.Success(MapResponse(state));
    }

    public async Task<ServiceResponse<IReadOnlyList<LinkStateResponse>>> ListAsync(
        string? nodeId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LinkState> states = string.IsNullOrWhiteSpace(nodeId)
            ? await _repository.ListAsync(cancellationToken).ConfigureAwait(false)
            : await _repository.ListByNodeAsync(nodeId, cancellationToken).ConfigureAwait(false);

        var response = states.Select(MapResponse).ToList();
        return ServiceResponse<IReadOnlyList<LinkStateResponse>>.Success(response);
    }

    public async Task<ServiceResponse<IReadOnlyList<MeshEventResponse>>> ListEventsAsync(
        int limit,
        DateTimeOffset? sinceUtc,
        CancellationToken cancellationToken)
    {
        var safeLimit = _eventOptions.NormalizeLimit(limit);
        var events = await _eventRepository.ListAsync(safeLimit, sinceUtc, cancellationToken).ConfigureAwait(false);
        var response = events.Select(evt => new MeshEventResponse
        {
            EventId = evt.Id,
            NodeId = evt.NodeId,
            Interface = evt.Interface.ToString().ToLowerInvariant(),
            EventType = evt.EventType.ToString().ToLowerInvariant(),
            Reason = evt.Reason,
            CreatedUtc = evt.CreatedUtc
        }).ToList();

        return ServiceResponse<IReadOnlyList<MeshEventResponse>>.Success(response);
    }

    private static LinkStateResponse MapResponse(LinkState state)
    {
        return new LinkStateResponse
        {
            NodeId = state.NodeId,
            Interface = state.Interface.ToString().ToLowerInvariant(),
            SignalQuality = state.SignalQuality,
            LatencyMs = state.LatencyMs,
            JitterMs = state.JitterMs,
            PacketLoss = state.PacketLoss,
            BandwidthKbps = state.BandwidthKbps,
            ThroughputKbps = state.ThroughputKbps,
            RssiDbm = state.RssiDbm,
            SnrDb = state.SnrDb,
            Status = state.Status.ToString().ToLowerInvariant(),
            UpdatedUtc = state.UpdatedUtc
        };
    }

    private static InterfaceKind ParseInterface(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return InterfaceKind.Unknown;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "wifi" => InterfaceKind.Wifi,
            "cellular" => InterfaceKind.Cellular,
            "ethernet" => InterfaceKind.Ethernet,
            "satellite" => InterfaceKind.Satellite,
            "lora" => InterfaceKind.LoRa,
            _ => InterfaceKind.Unknown
        };
    }

    private LinkStateStatus EvaluateStatus(LinkStateUpdateRequest request)
    {
        if (request.BandwidthKbps <= _thresholds.DownBandwidthKbps
            || request.SignalQuality <= _thresholds.DownSignalQuality
            || request.PacketLoss >= _thresholds.DownPacketLoss)
        {
            return LinkStateStatus.Down;
        }

        if (request.SignalQuality <= _thresholds.DegradedSignalQuality
            || request.PacketLoss >= _thresholds.DegradedPacketLoss
            || request.LatencyMs >= _thresholds.DegradedLatencyMs)
        {
            return LinkStateStatus.Degraded;
        }

        return LinkStateStatus.Healthy;
    }

    private async Task RecordEventIfChangedAsync(
        LinkState? previous,
        LinkState current,
        CancellationToken cancellationToken)
    {
        if (previous is null)
        {
            return;
        }

        if (previous.Status == current.Status)
        {
            return;
        }

        var eventType = current.Status switch
        {
            LinkStateStatus.Down => MeshEventType.LinkDown,
            LinkStateStatus.Degraded => MeshEventType.LinkDegraded,
            _ => MeshEventType.LinkRecovered
        };

        await _eventRepository.AppendAsync(new MeshEvent
        {
            NodeId = current.NodeId,
            Interface = current.Interface,
            EventType = eventType,
            Reason = $"Status changed from {previous.Status} to {current.Status}",
            CreatedUtc = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);

        if (_eventOptions.RetentionDays > 0)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-_eventOptions.RetentionDays);
            await _eventRepository.PruneAsync(cutoff, cancellationToken).ConfigureAwait(false);
        }
    }
}
