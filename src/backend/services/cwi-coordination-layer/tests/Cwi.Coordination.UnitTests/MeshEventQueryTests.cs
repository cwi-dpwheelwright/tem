using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Services;
using Cwi.Coordination.DataAccess;
using Cwi.Coordination.Domain.Entities;
using FluentAssertions;

namespace Cwi.Coordination.UnitTests;

public sealed class MeshEventQueryTests
{
    [Fact]
    public async Task ListEventsAsync_Filters_By_SinceUtc()
    {
        var topology = new InMemoryMeshTopologyRepository();
        var events = new InMemoryMeshEventRepository();
        var service = new MeshTopologyService(
            topology,
            events,
            new LinkStateThresholdOptions(),
            new MeshEventQueryOptions { RetentionDays = 0 });

        var oldEvent = new MeshEvent
        {
            Id = Guid.Parse("f3c2c653-0f2c-40fa-9bdf-6c9c1f4bb8f1"),
            NodeId = "node-1",
            Interface = InterfaceKind.Wifi,
            EventType = MeshEventType.LinkDown,
            Reason = "old",
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        var recentEvent = new MeshEvent
        {
            Id = Guid.Parse("5a3a0b4d-b0c7-4d7e-93ed-84f15978a6bb"),
            NodeId = "node-1",
            Interface = InterfaceKind.Wifi,
            EventType = MeshEventType.LinkRecovered,
            Reason = "recent",
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        await events.AppendAsync(oldEvent, CancellationToken.None);
        await events.AppendAsync(recentEvent, CancellationToken.None);

        var response = await service.ListEventsAsync(
            50,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().HaveCount(1);
        response.Data?.Single().EventId.Should().Be(recentEvent.Id);
    }

    [Fact]
    public async Task UpsertAsync_Prunes_Old_Events_When_Retention_Configured()
    {
        var topology = new InMemoryMeshTopologyRepository();
        var events = new InMemoryMeshEventRepository();
        var service = new MeshTopologyService(
            topology,
            events,
            new LinkStateThresholdOptions(),
            new MeshEventQueryOptions { RetentionDays = 1 });

        await events.AppendAsync(new MeshEvent
        {
            NodeId = "node-stale",
            Interface = InterfaceKind.Wifi,
            EventType = MeshEventType.LinkDown,
            Reason = "stale",
            CreatedUtc = DateTimeOffset.UtcNow.AddDays(-10)
        }, CancellationToken.None);

        await service.UpsertAsync(new LinkStateUpdateRequest
        {
            NodeId = "node-1",
            Interface = "wifi",
            SignalQuality = 0.9,
            LatencyMs = 50,
            PacketLoss = 0.01,
            BandwidthKbps = 1000
        }, CancellationToken.None);

        await service.UpsertAsync(new LinkStateUpdateRequest
        {
            NodeId = "node-1",
            Interface = "wifi",
            SignalQuality = 0.01,
            LatencyMs = 2000,
            PacketLoss = 0.9,
            BandwidthKbps = 0
        }, CancellationToken.None);

        var remaining = await events.ListAsync(10, null, CancellationToken.None);
        remaining.Should().NotContain(evt => evt.Reason == "stale");
    }
}
