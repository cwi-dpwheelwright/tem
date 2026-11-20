using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Services;
using Cwi.Coordination.DataAccess;
using FluentAssertions;

namespace Cwi.Coordination.UnitTests;

public sealed class RoutingHintServiceTests
{
    [Fact]
    public async Task GetHintAsync_Prefers_Bandwidth_For_High_Complexity()
    {
        var topology = new InMemoryMeshTopologyRepository();
        var survival = new InMemorySurvivalModeRepository();
        var options = new RoutingHintOptions();
        var engine = new DefaultRoutingHintEngine(options);
        var ledgerRepository = new InMemoryLedgerRepository();
        var ledger = new LedgerService(ledgerRepository);
        var service = new RoutingHintService(topology, engine, survival, ledger);

        await topology.UpsertAsync(new Cwi.Coordination.Domain.Entities.LinkState
        {
            NodeId = "node-1",
            Interface = Cwi.Coordination.Domain.Entities.InterfaceKind.Wifi,
            SignalQuality = 0.9,
            LatencyMs = 900,
            PacketLoss = 0.05,
            BandwidthKbps = 1500,
            Status = Cwi.Coordination.Domain.Entities.LinkStateStatus.Healthy
        }, CancellationToken.None);

        await topology.UpsertAsync(new Cwi.Coordination.Domain.Entities.LinkState
        {
            NodeId = "node-1",
            Interface = Cwi.Coordination.Domain.Entities.InterfaceKind.Ethernet,
            SignalQuality = 0.7,
            LatencyMs = 80,
            PacketLoss = 0.02,
            BandwidthKbps = 20000,
            Status = Cwi.Coordination.Domain.Entities.LinkStateStatus.Healthy
        }, CancellationToken.None);

        var response = await service.GetHintAsync(new RoutingHintRequest
        {
            NodeId = "node-1",
            TaskComplexity = "high"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data?.PreferredInterface.Should().Be("ethernet");
        response.Data?.TtlSeconds.Should().Be(options.DefaultTtlSeconds);

        var events = await ledgerRepository.ListAsync(5, CancellationToken.None);
        events.Should().ContainSingle(evt => evt.EventType == "routing.hint");
    }
}

public sealed class BackhaulPolicyServiceTests
{
    [Fact]
    public async Task EvaluateAsync_Blocks_Unauthenticated_When_Required()
    {
        var repo = new InMemoryBackhaulPolicyRepository();
        var ledgerRepo = new InMemoryLedgerRepository();
        var service = new BackhaulPolicyService(repo, new LedgerService(ledgerRepo));

        await service.SetPolicyAsync(new BackhaulPolicyRequest
        {
            RequireAuthenticatedChannel = true,
            AllowedInterfaces = new[] { "wifi", "ethernet" }
        }, CancellationToken.None);

        var response = await service.EvaluateAsync(new BackhaulDecisionRequest
        {
            Interface = "wifi",
            Authenticated = false
        }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(403);

        var events = await ledgerRepo.ListAsync(5, CancellationToken.None);
        events.Should().ContainSingle(evt => evt.EventType == "backhaul.decision");
    }
}

public sealed class SurvivalModeServiceTests
{
    [Fact]
    public async Task SetAsync_Requires_Reason_When_Enabling()
    {
        var repo = new InMemorySurvivalModeRepository();
        var ledgerRepo = new InMemoryLedgerRepository();
        var service = new SurvivalModeService(repo, new LedgerService(ledgerRepo));

        var response = await service.SetAsync(new SurvivalModeRequest
        {
            Enabled = true,
            Reason = ""
        }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);

        var events = await ledgerRepo.ListAsync(5, CancellationToken.None);
        events.Should().BeEmpty();
    }
}

public sealed class TopologyHealthServiceTests
{
    [Fact]
    public async Task GetHealthAsync_Reports_Degraded_When_Link_Down()
    {
        var repo = new InMemoryMeshTopologyRepository();
        var service = new TopologyHealthService(repo, new TopologyHealthOptions());

        await repo.UpsertAsync(new Cwi.Coordination.Domain.Entities.LinkState
        {
            NodeId = "node-1",
            Interface = Cwi.Coordination.Domain.Entities.InterfaceKind.Wifi,
            SignalQuality = 0.8,
            LatencyMs = 120,
            PacketLoss = 0.02,
            BandwidthKbps = 1000,
            Status = Cwi.Coordination.Domain.Entities.LinkStateStatus.Healthy
        }, CancellationToken.None);

        await repo.UpsertAsync(new Cwi.Coordination.Domain.Entities.LinkState
        {
            NodeId = "node-2",
            Interface = Cwi.Coordination.Domain.Entities.InterfaceKind.Cellular,
            SignalQuality = 0.0,
            LatencyMs = 2000,
            PacketLoss = 0.9,
            BandwidthKbps = 0,
            Status = Cwi.Coordination.Domain.Entities.LinkStateStatus.Down
        }, CancellationToken.None);

        var response = await service.GetHealthAsync(CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data?.Status.Should().Be("down");
        response.Data?.DownNodes.Should().Be(1);
        response.Data?.DegradedNodes.Should().Be(0);
    }
}

public sealed class RerouteGuidanceServiceTests
{
    [Fact]
    public async Task GetGuidanceAsync_Emits_Reroute_Event()
    {
        var topology = new InMemoryMeshTopologyRepository();
        var events = new InMemoryMeshEventRepository();
        var survival = new InMemorySurvivalModeRepository();
        var options = new RoutingHintOptions();
        var engine = new DefaultRoutingHintEngine(options);
        var ledgerRepo = new InMemoryLedgerRepository();
        var service = new RerouteGuidanceService(topology, events, survival, engine, new LedgerService(ledgerRepo));

        await topology.UpsertAsync(new Cwi.Coordination.Domain.Entities.LinkState
        {
            NodeId = "node-1",
            Interface = Cwi.Coordination.Domain.Entities.InterfaceKind.Ethernet,
            SignalQuality = 0.9,
            LatencyMs = 50,
            PacketLoss = 0.01,
            BandwidthKbps = 10000,
            Status = Cwi.Coordination.Domain.Entities.LinkStateStatus.Healthy
        }, CancellationToken.None);

        var response = await service.GetGuidanceAsync(new RerouteGuidanceRequest
        {
            NodeId = "node-1",
            CurrentInterface = "wifi",
            TaskComplexity = "high"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data?.PreferredInterface.Should().Be("ethernet");

        var meshEvents = await events.ListAsync(10, null, CancellationToken.None);
        meshEvents.Should().ContainSingle(evt => evt.EventType == Cwi.Coordination.Domain.Entities.MeshEventType.RerouteSuggested);

        var ledgerEvents = await ledgerRepo.ListAsync(10, CancellationToken.None);
        ledgerEvents.Should().ContainSingle(evt => evt.EventType == "reroute.guidance");
    }
}

public sealed class MeshTopologySimulationTests
{
    [Fact]
    public async Task UpsertAsync_Emits_Events_For_Partition_Churn()
    {
        var topology = new InMemoryMeshTopologyRepository();
        var events = new InMemoryMeshEventRepository();
        var service = new MeshTopologyService(
            topology,
            events,
            new LinkStateThresholdOptions(),
            new MeshEventQueryOptions { RetentionDays = 0 });

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
            SignalQuality = 0.02,
            LatencyMs = 2000,
            PacketLoss = 0.9,
            BandwidthKbps = 0
        }, CancellationToken.None);

        await service.UpsertAsync(new LinkStateUpdateRequest
        {
            NodeId = "node-1",
            Interface = "wifi",
            SignalQuality = 0.8,
            LatencyMs = 80,
            PacketLoss = 0.02,
            BandwidthKbps = 1200
        }, CancellationToken.None);

        var emitted = await events.ListAsync(10, null, CancellationToken.None);
        emitted.Select(evt => evt.EventType).Should().Contain(new[]
        {
            Cwi.Coordination.Domain.Entities.MeshEventType.LinkDown,
            Cwi.Coordination.Domain.Entities.MeshEventType.LinkRecovered
        });
    }
}
