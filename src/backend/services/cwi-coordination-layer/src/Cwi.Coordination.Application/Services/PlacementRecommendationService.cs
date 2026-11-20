using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;
using DomainPlacementEnvironmentMetrics = Cwi.Coordination.Domain.Entities.PlacementEnvironmentMetrics;

namespace Cwi.Coordination.Application.Services;

public sealed class PlacementRecommendationService
{
    private readonly IPlacementMetricsRepository _metricsRepository;
    private readonly ISurvivalModeRepository _survivalRepository;
    private readonly LedgerService _ledgerService;
    private readonly PlacementOptimizationOptions _options;

    public PlacementRecommendationService(
        IPlacementMetricsRepository metricsRepository,
        ISurvivalModeRepository survivalRepository,
        LedgerService ledgerService,
        PlacementOptimizationOptions options)
    {
        _metricsRepository = metricsRepository;
        _survivalRepository = survivalRepository;
        _ledgerService = ledgerService;
        _options = options;
    }

    public async Task<ServiceResponse<PlacementRecommendationResponse>> RecommendAsync(
        PlacementRecommendationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return ServiceResponse<PlacementRecommendationResponse>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id is required."),
                400,
                "tenant_id is required");
        }

        var metrics = await LoadMetricsAsync(request, cancellationToken).ConfigureAwait(false);
        if (metrics.Count == 0)
        {
            return ServiceResponse<PlacementRecommendationResponse>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "no placement metrics available"),
                503,
                "no placement metrics available");
        }

        var survivalBias = await ResolveSurvivalBiasAsync(request, cancellationToken).ConfigureAwait(false);
        var scored = ScoreMetrics(metrics, request, survivalBias);
        if (scored.Count == 0)
        {
            return ServiceResponse<PlacementRecommendationResponse>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "no placement candidates meet constraints"),
                422,
                "no placement candidates meet constraints");
        }

        var response = BuildResponse(request, survivalBias, scored);
        await AppendLedgerEventsAsync(response, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<PlacementRecommendationResponse>.Success(response);
    }

    private async Task<IReadOnlyList<PlacementMetric>> LoadMetricsAsync(
        PlacementRecommendationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.NodeIds is { Count: > 0 })
        {
            var results = new List<PlacementMetric>();
            foreach (var nodeId in request.NodeIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(nodeId))
                {
                    continue;
                }
                var metrics = await _metricsRepository.ListAsync(nodeId, 1, cancellationToken).ConfigureAwait(false);
                var latest = metrics
                    .Where(metric => MatchesScenario(metric, request.ScenarioId))
                    .OrderByDescending(metric => metric.ObservedUtc)
                    .FirstOrDefault();
                if (latest is not null)
                {
                    results.Add(latest);
                }
            }

            return results;
        }

        var limit = request.Limit ?? _options.MaxMetricsLookback;
        var all = await _metricsRepository.ListAsync(null, limit, cancellationToken).ConfigureAwait(false);
        return all
            .Where(metric => MatchesScenario(metric, request.ScenarioId))
            .GroupBy(metric => metric.NodeId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(metric => metric.ObservedUtc).First())
            .ToList();
    }

    private static bool MatchesScenario(PlacementMetric metric, string? scenarioId)
    {
        return string.IsNullOrWhiteSpace(scenarioId)
            || string.Equals(metric.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> ResolveSurvivalBiasAsync(
        PlacementRecommendationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SurvivalModeBias.HasValue)
        {
            return request.SurvivalModeBias.Value;
        }

        var state = await _survivalRepository.GetAsync(cancellationToken).ConfigureAwait(false);
        return state.Enabled;
    }

    private List<ScoredMetric> ScoreMetrics(
        IReadOnlyList<PlacementMetric> metrics,
        PlacementRecommendationRequest request,
        bool survivalBias)
    {
        var results = new List<ScoredMetric>();
        foreach (var metric in metrics)
        {
            var (score, confidence, rationale) = ComputeScore(metric, request.Constraints, survivalBias);
            var location = ResolveLocation(metric, out var locationReason);
            if (!string.IsNullOrWhiteSpace(locationReason))
            {
                rationale = string.IsNullOrWhiteSpace(rationale)
                    ? locationReason
                    : $"{rationale} {locationReason}".Trim();
            }

            results.Add(new ScoredMetric(metric, score, confidence, rationale, location));
        }

        return results
            .OrderByDescending(result => result.Score)
            .ToList();
    }

    private PlacementRecommendationResponse BuildResponse(
        PlacementRecommendationRequest request,
        bool survivalBias,
        IReadOnlyList<ScoredMetric> scored)
    {
        var nodes = scored.Select(result => new PlacementRecommendationNode
        {
            NodeId = result.Metric.NodeId,
            RecommendedLocation = result.Location,
            Confidence = Math.Round(result.Confidence, 3),
            Rationale = result.Rationale,
            NodeConstraints = request.Constraints
        }).ToList();

        var score = scored.Count == 0
            ? 0
            : scored.Average(result => result.Score);

        var averageConfidence = scored.Count == 0
            ? 0
            : scored.Average(result => result.Confidence);

        var governance = EvaluateGovernance(averageConfidence);
        var metricsIds = scored.Select(result => result.Metric.MetricsId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PlacementRecommendationResponse
        {
            RecommendationId = $"mpr-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}",
            TenantId = request.TenantId,
            ScenarioId = request.ScenarioId,
            GeneratedUtc = DateTimeOffset.UtcNow,
            SurvivalModeBias = survivalBias,
            Score = Math.Round(score, 3),
            Constraints = request.Constraints,
            Nodes = nodes,
            Rationale = survivalBias
                ? "Heuristic placement favors resilience under survival-mode bias."
                : "Heuristic placement balances link quality and environment conditions.",
            Governance = governance,
            Provenance = new PlacementProvenance
            {
                MetricsIds = metricsIds,
                NodeCount = nodes.Count,
                Source = request.Metadata?.TryGetValue("source", out var source) == true ? source?.ToString() : null
            },
            Metadata = new Dictionary<string, object?>
            {
                ["model_version"] = _options.ModelVersion,
                ["average_confidence"] = Math.Round(averageConfidence, 3)
            }
        };
    }

    private PlacementGovernanceSummary EvaluateGovernance(double averageConfidence)
    {
        var status = _options.RequireGovernanceApproval || averageConfidence < _options.MinConfidence
            ? "pending"
            : "approved";

        var notes = status == "pending"
            ? $"Requires governance review (avg confidence {averageConfidence:0.00})."
            : "Auto-approved by policy.";

        return new PlacementGovernanceSummary
        {
            Status = status,
            PolicyVersion = _options.PolicyVersion,
            DecisionId = $"pg-{Guid.NewGuid():N}",
            EvaluatedUtc = DateTimeOffset.UtcNow,
            Notes = notes
        };
    }

    private async Task AppendLedgerEventsAsync(
        PlacementRecommendationResponse response,
        CancellationToken cancellationToken)
    {
        await _ledgerService.AppendAsync("placement.recommendation.generated", new
        {
            response.RecommendationId,
            response.TenantId,
            response.ScenarioId,
            response.SurvivalModeBias,
            response.Score,
            NodeCount = response.Nodes.Count,
            response.Governance?.Status,
            response.Governance?.DecisionId,
            response.Metadata
        }, cancellationToken).ConfigureAwait(false);

        await _ledgerService.AppendAsync("placement.governance.evaluated", new
        {
            response.RecommendationId,
            response.Governance?.Status,
            response.Governance?.PolicyVersion,
            response.Governance?.DecisionId,
            MinConfidence = _options.MinConfidence
        }, cancellationToken).ConfigureAwait(false);
    }

    private static (double Score, double Confidence, string? Rationale) ComputeScore(
        PlacementMetric metric,
        PlacementRecommendationConstraints? constraints,
        bool survivalBias)
    {
        var latencyScore = ScoreLatency(metric.Radio.LatencyMs, constraints?.MaxLatencyMs);
        var packetLossScore = Clamp01(1 - metric.Radio.PacketLoss);
        var rssiScore = Clamp01((metric.Radio.RssiDbm + 100) / 70);
        var snrScore = Clamp01(metric.Radio.SnrDb / 30);
        var throughputScore = ScoreThroughput(metric.Radio.ThroughputMbps, metric.Radio.BandwidthMbps);
        var environmentScore = ScoreEnvironment(metric.Environment);

        var (signalWeight, latencyWeight, lossWeight, throughputWeight, environmentWeight) = survivalBias
            ? (0.3, 0.25, 0.2, 0.1, 0.15)
            : (0.25, 0.2, 0.15, 0.2, 0.2);

        var score = (rssiScore + snrScore) / 2 * signalWeight
            + latencyScore * latencyWeight
            + packetLossScore * lossWeight
            + throughputScore * throughputWeight
            + environmentScore * environmentWeight;

        var confidence = Clamp01(metric.Uncertainty.Confidence);
        var rationaleParts = new List<string>();

        if (constraints is not null)
        {
            if (constraints.MaxLatencyMs.HasValue && metric.Radio.LatencyMs > constraints.MaxLatencyMs.Value)
            {
                confidence *= 0.8;
                score *= 0.8;
                rationaleParts.Add("Latency exceeds max constraint.");
            }
            if (constraints.MinRssi.HasValue && metric.Radio.RssiDbm < constraints.MinRssi.Value)
            {
                confidence *= 0.8;
                score *= 0.85;
                rationaleParts.Add("RSSI below minimum.");
            }
            if (constraints.MinSnr.HasValue && metric.Radio.SnrDb < constraints.MinSnr.Value)
            {
                confidence *= 0.8;
                score *= 0.85;
                rationaleParts.Add("SNR below minimum.");
            }
        }

        return (Clamp01(score), Clamp01(confidence), rationaleParts.Count > 0 ? string.Join(" ", rationaleParts) : null);
    }

    private static double ScoreLatency(double latencyMs, double? maxLatency)
    {
        if (maxLatency.HasValue && maxLatency.Value > 0)
        {
            return Clamp01(1 - (latencyMs / maxLatency.Value));
        }

        return Clamp01(1 - (latencyMs / 200));
    }

    private static double ScoreThroughput(double throughputMbps, double bandwidthMbps)
    {
        if (bandwidthMbps <= 0)
        {
            return Clamp01(throughputMbps / 100);
        }

        return Clamp01(throughputMbps / bandwidthMbps);
    }

    private static double ScoreEnvironment(DomainPlacementEnvironmentMetrics environment)
    {
        var obstructionPenalty = environment.ObstructionIndex ?? 0;
        var precipitationPenalty = environment.PrecipitationMm.HasValue
            ? Math.Min(environment.PrecipitationMm.Value / 50, 1)
            : 0;

        var penalty = (0.6 * Clamp01(obstructionPenalty)) + (0.4 * Clamp01(precipitationPenalty));
        return Clamp01(1 - penalty);
    }

    private static PlacementRecommendationLocation ResolveLocation(
        PlacementMetric metric,
        out string? rationale)
    {
        rationale = null;
        if (metric.Metadata is null || metric.Metadata.Count == 0)
        {
            rationale = "No location metadata available.";
            return new PlacementRecommendationLocation();
        }

        var lat = TryGetDouble(metric.Metadata, "lat", "latitude");
        var lon = TryGetDouble(metric.Metadata, "lon", "longitude");
        var altitude = TryGetDouble(metric.Metadata, "altitude_m", "altitude");

        if (!lat.HasValue || !lon.HasValue)
        {
            rationale = "Location metadata incomplete.";
            return new PlacementRecommendationLocation();
        }

        return new PlacementRecommendationLocation
        {
            Lat = lat.Value,
            Lon = lon.Value,
            AltitudeM = altitude
        };
    }

    private static double? TryGetDouble(
        IReadOnlyDictionary<string, object?> metadata,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!metadata.TryGetValue(key, out var value))
            {
                continue;
            }

            if (TryConvertDouble(value, out var number))
            {
                return number;
            }
        }

        return null;
    }

    private static bool TryConvertDouble(object? value, out double number)
    {
        switch (value)
        {
            case null:
                number = 0;
                return false;
            case double d:
                number = d;
                return true;
            case float f:
                number = f;
                return true;
            case decimal dec:
                number = (double)dec;
                return true;
            case int i:
                number = i;
                return true;
            case long l:
                number = l;
                return true;
            case JsonElement element:
                return TryConvertJsonElement(element, out number);
            default:
                if (double.TryParse(value.ToString(), out number))
                {
                    return true;
                }
                return false;
        }
    }

    private static bool TryConvertJsonElement(JsonElement element, out double number)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (element.TryGetDouble(out number))
                {
                    return true;
                }
                break;
            case JsonValueKind.String:
                if (double.TryParse(element.GetString(), out number))
                {
                    return true;
                }
                break;
        }

        number = 0;
        return false;
    }

    private static double Clamp01(double value) => Math.Max(0, Math.Min(1, value));

    private sealed record ScoredMetric(
        PlacementMetric Metric,
        double Score,
        double Confidence,
        string? Rationale,
        PlacementRecommendationLocation Location);
}
