using System.Text.Json.Serialization;

namespace Cwi.Coordination.Application.Models;

public sealed class PlacementMetricsRequest
{
    [JsonPropertyName("metrics_id")]
    public string MetricsId { get; set; } = string.Empty;

    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("observed_utc")]
    public DateTimeOffset ObservedUtc { get; set; }

    [JsonPropertyName("scenario_id")]
    public string? ScenarioId { get; set; }

    [JsonPropertyName("radio")]
    public PlacementRadioMetrics Radio { get; set; } = new();

    [JsonPropertyName("environment")]
    public PlacementEnvironmentMetrics Environment { get; set; } = new();

    [JsonPropertyName("uncertainty")]
    public PlacementUncertaintyMetrics Uncertainty { get; set; } = new();

    [JsonPropertyName("metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}

public sealed class PlacementMetricsResponse
{
    [JsonPropertyName("metrics_id")]
    public string MetricsId { get; set; } = string.Empty;

    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("observed_utc")]
    public DateTimeOffset ObservedUtc { get; set; }

    [JsonPropertyName("scenario_id")]
    public string? ScenarioId { get; set; }

    [JsonPropertyName("radio")]
    public PlacementRadioMetrics Radio { get; set; } = new();

    [JsonPropertyName("environment")]
    public PlacementEnvironmentMetrics Environment { get; set; } = new();

    [JsonPropertyName("uncertainty")]
    public PlacementUncertaintyMetrics Uncertainty { get; set; } = new();

    [JsonPropertyName("metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }
}

public sealed class PlacementRadioMetrics
{
    [JsonPropertyName("latency_ms")]
    public double LatencyMs { get; set; }

    [JsonPropertyName("jitter_ms")]
    public double? JitterMs { get; set; }

    [JsonPropertyName("packet_loss")]
    public double PacketLoss { get; set; }

    [JsonPropertyName("rssi_dbm")]
    public double RssiDbm { get; set; }

    [JsonPropertyName("snr_db")]
    public double SnrDb { get; set; }

    [JsonPropertyName("throughput_mbps")]
    public double ThroughputMbps { get; set; }

    [JsonPropertyName("bandwidth_mbps")]
    public double BandwidthMbps { get; set; }

    [JsonPropertyName("noise_floor_dbm")]
    public double? NoiseFloorDbm { get; set; }

    [JsonPropertyName("interference_index")]
    public double? InterferenceIndex { get; set; }
}

public sealed class PlacementEnvironmentMetrics
{
    [JsonPropertyName("temperature_c")]
    public double TemperatureC { get; set; }

    [JsonPropertyName("humidity_pct")]
    public double HumidityPct { get; set; }

    [JsonPropertyName("wind_speed_mps")]
    public double? WindSpeedMps { get; set; }

    [JsonPropertyName("precipitation_mm")]
    public double? PrecipitationMm { get; set; }

    [JsonPropertyName("terrain")]
    public string? Terrain { get; set; }

    [JsonPropertyName("obstruction_index")]
    public double? ObstructionIndex { get; set; }
}

public sealed class PlacementUncertaintyMetrics
{
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("rssi_stddev")]
    public double? RssiStddev { get; set; }

    [JsonPropertyName("snr_stddev")]
    public double? SnrStddev { get; set; }

    [JsonPropertyName("latency_stddev")]
    public double? LatencyStddev { get; set; }

    [JsonPropertyName("throughput_stddev")]
    public double? ThroughputStddev { get; set; }

    [JsonPropertyName("location_stddev_m")]
    public double? LocationStddevM { get; set; }
}
