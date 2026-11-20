using Cwi.Coordination.Application.Services;
using Cwi.Coordination.Application.Transport;
using Cwi.Coordination.DataAccess;
using FluentAssertions;

namespace Cwi.Coordination.UnitTests;

public sealed class LinkMetricsIngestServiceTests
{
    [Fact]
    public async Task IngestAsync_Stores_LinkStates_From_Simulated_Adapter()
    {
        var topologyRepo = new InMemoryMeshTopologyRepository();
        var eventRepo = new InMemoryMeshEventRepository();
        var topologyService = new MeshTopologyService(
            topologyRepo,
            eventRepo,
            new LinkStateThresholdOptions(),
            new MeshEventQueryOptions { RetentionDays = 0 });
        var ingestService = new LinkMetricsIngestService(topologyService, new LinkMetricsNormalizationOptions());
        var adapter = new SimulatedLinkMetricsTransportAdapter();

        var request = await adapter.BuildSampleAsync("node-1", CancellationToken.None);
        var response = await ingestService.IngestAsync(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data?.Accepted.Should().Be(3);

        var states = await topologyRepo.ListByNodeAsync("node-1", CancellationToken.None);
        states.Should().HaveCount(3);
        states.Should().OnlyContain(state => state.SignalQuality >= 0 && state.SignalQuality <= 1);
    }

    [Fact]
    public async Task IngestAsync_Requires_NodeId()
    {
        var topologyRepo = new InMemoryMeshTopologyRepository();
        var eventRepo = new InMemoryMeshEventRepository();
        var topologyService = new MeshTopologyService(
            topologyRepo,
            eventRepo,
            new LinkStateThresholdOptions(),
            new MeshEventQueryOptions { RetentionDays = 0 });
        var ingestService = new LinkMetricsIngestService(topologyService, new LinkMetricsNormalizationOptions());

        var response = await ingestService.IngestAsync(new Cwi.Coordination.Application.Models.LinkMetricsTransportRequest(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
    }
}
