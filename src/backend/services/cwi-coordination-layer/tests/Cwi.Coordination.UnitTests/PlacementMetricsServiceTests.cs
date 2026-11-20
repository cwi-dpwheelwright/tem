using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Services;
using Cwi.Coordination.DataAccess;
using FluentAssertions;

namespace Cwi.Coordination.UnitTests;

public sealed class PlacementMetricsServiceTests
{
    [Fact]
    public async Task IngestAsync_Requires_NodeId()
    {
        var repo = new InMemoryPlacementMetricsRepository();
        var service = new PlacementMetricsService(repo);

        var response = await service.IngestAsync(new PlacementMetricsRequest
        {
            NodeId = "",
            Radio = new PlacementRadioMetrics(),
            Environment = new PlacementEnvironmentMetrics(),
            Uncertainty = new PlacementUncertaintyMetrics { Confidence = 0.5 }
        }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task IngestAsync_Persists_Metrics_And_Lists_By_Node()
    {
        var repo = new InMemoryPlacementMetricsRepository();
        var service = new PlacementMetricsService(repo);

        var ingest = await service.IngestAsync(new PlacementMetricsRequest
        {
            NodeId = "node-17",
            MetricsId = "mpm-1",
            ObservedUtc = DateTimeOffset.UtcNow,
            Radio = new PlacementRadioMetrics
            {
                LatencyMs = 80,
                PacketLoss = 0.01,
                RssiDbm = -70,
                SnrDb = 15,
                ThroughputMbps = 10,
                BandwidthMbps = 12
            },
            Environment = new PlacementEnvironmentMetrics
            {
                TemperatureC = 12,
                HumidityPct = 70
            },
            Uncertainty = new PlacementUncertaintyMetrics
            {
                Confidence = 0.8
            }
        }, CancellationToken.None);

        ingest.IsSuccess.Should().BeTrue();

        var list = await service.ListAsync("node-17", 10, CancellationToken.None);
        list.IsSuccess.Should().BeTrue();
        list.Data.Should().ContainSingle(metric => metric.MetricsId == "mpm-1");
    }
}
