using System.Text.Json.Serialization;

namespace Cwi.Coordination.Application.Models;

public sealed class PlacementRecommendationRequest
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("scenario_id")]
    public string? ScenarioId { get; set; }

    [JsonPropertyName("survival_mode_bias")]
    public bool? SurvivalModeBias { get; set; }

    [JsonPropertyName("constraints")]
    public PlacementRecommendationConstraints? Constraints { get; set; }

    [JsonPropertyName("node_ids")]
    public IReadOnlyList<string>? NodeIds { get; set; }

    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}

public sealed class PlacementRecommendationResponse
{
    [JsonPropertyName("recommendation_id")]
    public string RecommendationId { get; set; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("generated_utc")]
    public DateTimeOffset GeneratedUtc { get; set; }

    [JsonPropertyName("scenario_id")]
    public string? ScenarioId { get; set; }

    [JsonPropertyName("survival_mode_bias")]
    public bool SurvivalModeBias { get; set; }

    [JsonPropertyName("score")]
    public double Score { get; set; }

    [JsonPropertyName("constraints")]
    public PlacementRecommendationConstraints? Constraints { get; set; }

    [JsonPropertyName("nodes")]
    public IReadOnlyList<PlacementRecommendationNode> Nodes { get; set; } = Array.Empty<PlacementRecommendationNode>();

    [JsonPropertyName("rationale")]
    public string? Rationale { get; set; }

    [JsonPropertyName("governance")]
    public PlacementGovernanceSummary? Governance { get; set; }

    [JsonPropertyName("provenance")]
    public PlacementProvenance? Provenance { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}

public sealed class PlacementRecommendationNode
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("recommended_location")]
    public PlacementRecommendationLocation RecommendedLocation { get; set; } = new();

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("rationale")]
    public string? Rationale { get; set; }

    [JsonPropertyName("node_constraints")]
    public PlacementRecommendationConstraints? NodeConstraints { get; set; }
}

public sealed class PlacementRecommendationLocation
{
    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lon")]
    public double Lon { get; set; }

    [JsonPropertyName("altitude_m")]
    public double? AltitudeM { get; set; }
}

public sealed class PlacementRecommendationConstraints
{
    [JsonPropertyName("max_latency_ms")]
    public double? MaxLatencyMs { get; set; }

    [JsonPropertyName("min_rssi")]
    public double? MinRssi { get; set; }

    [JsonPropertyName("min_snr")]
    public double? MinSnr { get; set; }
}

public sealed class PlacementGovernanceSummary
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "approved";

    [JsonPropertyName("policy_version")]
    public string PolicyVersion { get; set; } = "1.0";

    [JsonPropertyName("decision_id")]
    public string? DecisionId { get; set; }

    [JsonPropertyName("evaluated_utc")]
    public DateTimeOffset EvaluatedUtc { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public sealed class PlacementProvenance
{
    [JsonPropertyName("metrics_ids")]
    public IReadOnlyList<string> MetricsIds { get; set; } = Array.Empty<string>();

    [JsonPropertyName("node_count")]
    public int NodeCount { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }
}
