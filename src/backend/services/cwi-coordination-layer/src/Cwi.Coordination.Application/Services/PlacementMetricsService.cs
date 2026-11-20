using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Domain.Repositories;
using DomainEntities = Cwi.Coordination.Domain.Entities;

namespace Cwi.Coordination.Application.Services;

public sealed class PlacementMetricsService
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 500;

    private readonly IPlacementMetricsRepository _repository;

    public PlacementMetricsService(IPlacementMetricsRepository repository)
    {
        _repository = repository;
    }

    public async Task<ServiceResponse<PlacementMetricsResponse>> IngestAsync(
        PlacementMetricsRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.NodeId))
        {
            return ServiceResponse<PlacementMetricsResponse>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "node_id required."),
                statusCode: 400,
                message: "node_id required");
        }

        if (request.Radio is null || request.Environment is null || request.Uncertainty is null)
        {
            return ServiceResponse<PlacementMetricsResponse>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "radio, environment, and uncertainty required."),
                statusCode: 400,
                message: "metrics sections required");
        }

        var metricsId = string.IsNullOrWhiteSpace(request.MetricsId)
            ? $"mpm-{Guid.NewGuid():n}"
            : request.MetricsId.Trim();

        var observed = request.ObservedUtc == default
            ? DateTimeOffset.UtcNow
            : request.ObservedUtc;

        var metric = new DomainEntities.PlacementMetric
        {
            MetricsId = metricsId,
            NodeId = request.NodeId.Trim(),
            ObservedUtc = observed,
            ScenarioId = string.IsNullOrWhiteSpace(request.ScenarioId) ? null : request.ScenarioId.Trim(),
            Radio = MapRadio(request.Radio),
            Environment = MapEnvironment(request.Environment),
            Uncertainty = MapUncertainty(request.Uncertainty),
            Metadata = request.Metadata
        };

        await _repository.AddAsync(metric, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<PlacementMetricsResponse>.Success(Map(metric));
    }

    public async Task<ServiceResponse<IReadOnlyList<PlacementMetricsResponse>>> ListAsync(
        string? nodeId,
        int limit,
        CancellationToken cancellationToken)
    {
        var cappedLimit = limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);
        var metrics = await _repository.ListAsync(nodeId, cappedLimit, cancellationToken).ConfigureAwait(false);
        var response = metrics.Select(Map).ToList();
        return ServiceResponse<IReadOnlyList<PlacementMetricsResponse>>.Success(response);
    }

    private static DomainEntities.PlacementRadioMetrics MapRadio(PlacementRadioMetrics radio)
    {
        return new DomainEntities.PlacementRadioMetrics
        {
            LatencyMs = radio.LatencyMs,
            JitterMs = radio.JitterMs,
            PacketLoss = radio.PacketLoss,
            RssiDbm = radio.RssiDbm,
            SnrDb = radio.SnrDb,
            ThroughputMbps = radio.ThroughputMbps,
            BandwidthMbps = radio.BandwidthMbps,
            NoiseFloorDbm = radio.NoiseFloorDbm,
            InterferenceIndex = radio.InterferenceIndex
        };
    }

    private static DomainEntities.PlacementEnvironmentMetrics MapEnvironment(PlacementEnvironmentMetrics environment)
    {
        return new DomainEntities.PlacementEnvironmentMetrics
        {
            TemperatureC = environment.TemperatureC,
            HumidityPct = environment.HumidityPct,
            WindSpeedMps = environment.WindSpeedMps,
            PrecipitationMm = environment.PrecipitationMm,
            Terrain = environment.Terrain,
            ObstructionIndex = environment.ObstructionIndex
        };
    }

    private static DomainEntities.PlacementUncertaintyMetrics MapUncertainty(PlacementUncertaintyMetrics uncertainty)
    {
        return new DomainEntities.PlacementUncertaintyMetrics
        {
            Confidence = uncertainty.Confidence,
            RssiStddev = uncertainty.RssiStddev,
            SnrStddev = uncertainty.SnrStddev,
            LatencyStddev = uncertainty.LatencyStddev,
            ThroughputStddev = uncertainty.ThroughputStddev,
            LocationStddevM = uncertainty.LocationStddevM
        };
    }

    private static PlacementMetricsResponse Map(DomainEntities.PlacementMetric metric)
    {
        return new PlacementMetricsResponse
        {
            MetricsId = metric.MetricsId,
            NodeId = metric.NodeId,
            ObservedUtc = metric.ObservedUtc,
            ScenarioId = metric.ScenarioId,
            Radio = new PlacementRadioMetrics
            {
                LatencyMs = metric.Radio.LatencyMs,
                JitterMs = metric.Radio.JitterMs,
                PacketLoss = metric.Radio.PacketLoss,
                RssiDbm = metric.Radio.RssiDbm,
                SnrDb = metric.Radio.SnrDb,
                ThroughputMbps = metric.Radio.ThroughputMbps,
                BandwidthMbps = metric.Radio.BandwidthMbps,
                NoiseFloorDbm = metric.Radio.NoiseFloorDbm,
                InterferenceIndex = metric.Radio.InterferenceIndex
            },
            Environment = new PlacementEnvironmentMetrics
            {
                TemperatureC = metric.Environment.TemperatureC,
                HumidityPct = metric.Environment.HumidityPct,
                WindSpeedMps = metric.Environment.WindSpeedMps,
                PrecipitationMm = metric.Environment.PrecipitationMm,
                Terrain = metric.Environment.Terrain,
                ObstructionIndex = metric.Environment.ObstructionIndex
            },
            Uncertainty = new PlacementUncertaintyMetrics
            {
                Confidence = metric.Uncertainty.Confidence,
                RssiStddev = metric.Uncertainty.RssiStddev,
                SnrStddev = metric.Uncertainty.SnrStddev,
                LatencyStddev = metric.Uncertainty.LatencyStddev,
                ThroughputStddev = metric.Uncertainty.ThroughputStddev,
                LocationStddevM = metric.Uncertainty.LocationStddevM
            },
            Metadata = metric.Metadata
        };
    }
}
