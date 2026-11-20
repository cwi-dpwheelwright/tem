using Cwi.Common.Errors;
using Cwi.Coordination.Application;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Services;
using Cwi.Coordination.DataAccess;
using Cwi.Coordination.Domain.Entities;
using FluentAssertions;
using DomainPlacementEnvironmentMetrics = Cwi.Coordination.Domain.Entities.PlacementEnvironmentMetrics;
using DomainPlacementRadioMetrics = Cwi.Coordination.Domain.Entities.PlacementRadioMetrics;
using DomainPlacementUncertaintyMetrics = Cwi.Coordination.Domain.Entities.PlacementUncertaintyMetrics;

namespace Cwi.Coordination.UnitTests;

public sealed class PlacementRecommendationServiceTests
{
    [Fact]
    public async Task RecommendAsync_Returns_Recommendation_With_Provenance()
    {
        var metricsRepo = new InMemoryPlacementMetricsRepository();
        var survivalRepo = new InMemorySurvivalModeRepository();
        var ledgerRepo = new InMemoryLedgerRepository();
        var ledger = new LedgerService(ledgerRepo);
        var options = new PlacementOptimizationOptions { ModelVersion = "heuristic-v1" };
        var service = new PlacementRecommendationService(metricsRepo, survivalRepo, ledger, options);

        await metricsRepo.AddAsync(new PlacementMetric
        {
            MetricsId = "mpm-1",
            NodeId = "node-1",
            ObservedUtc = DateTimeOffset.UtcNow,
            Radio = new DomainPlacementRadioMetrics
            {
                LatencyMs = 90,
                PacketLoss = 0.03,
                RssiDbm = -65,
                SnrDb = 18,
                ThroughputMbps = 8,
                BandwidthMbps = 12
            },
            Environment = new DomainPlacementEnvironmentMetrics
            {
                TemperatureC = 10,
                HumidityPct = 50,
                ObstructionIndex = 0.2
            },
            Uncertainty = new DomainPlacementUncertaintyMetrics
            {
                Confidence = 0.8
            },
            Metadata = new Dictionary<string, object?>
            {
                ["lat"] = 37.77,
                ["lon"] = -122.41
            }
        }, CancellationToken.None);

        var response = await service.RecommendAsync(new PlacementRecommendationRequest
        {
            TenantId = "tenant-1"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Nodes.Should().HaveCount(1);
        response.Data.Provenance.Should().NotBeNull();
        response.Data.Provenance!.MetricsIds.Should().Contain("mpm-1");
        response.Data.Metadata.Should().ContainKey("model_version");
    }

    [Fact]
    public async Task RecommendAsync_Uses_SurvivalMode_When_Not_Specified()
    {
        var metricsRepo = new InMemoryPlacementMetricsRepository();
        var survivalRepo = new InMemorySurvivalModeRepository();
        var ledgerRepo = new InMemoryLedgerRepository();
        var ledger = new LedgerService(ledgerRepo);
        var service = new PlacementRecommendationService(metricsRepo, survivalRepo, ledger, new PlacementOptimizationOptions());

        await survivalRepo.SetAsync(new SurvivalModeState { Enabled = true }, CancellationToken.None);
        await metricsRepo.AddAsync(new PlacementMetric
        {
            MetricsId = "mpm-2",
            NodeId = "node-2",
            ObservedUtc = DateTimeOffset.UtcNow,
            Radio = new DomainPlacementRadioMetrics
            {
                LatencyMs = 70,
                PacketLoss = 0.01,
                RssiDbm = -60,
                SnrDb = 20,
                ThroughputMbps = 9,
                BandwidthMbps = 12
            },
            Environment = new DomainPlacementEnvironmentMetrics
            {
                TemperatureC = 12,
                HumidityPct = 55
            },
            Uncertainty = new DomainPlacementUncertaintyMetrics
            {
                Confidence = 0.75
            }
        }, CancellationToken.None);

        var response = await service.RecommendAsync(new PlacementRecommendationRequest
        {
            TenantId = "tenant-2"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.SurvivalModeBias.Should().BeTrue();
    }

    [Fact]
    public async Task RecommendAsync_Fails_When_TenantId_Missing()
    {
        var service = new PlacementRecommendationService(
            new InMemoryPlacementMetricsRepository(),
            new InMemorySurvivalModeRepository(),
            new LedgerService(new InMemoryLedgerRepository()),
            new PlacementOptimizationOptions());

        var response = await service.RecommendAsync(new PlacementRecommendationRequest
        {
            TenantId = ""
        }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
        response.Error?.Code.Should().Be(ErrorCodes.InvalidRequest);
    }
}
