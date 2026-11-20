namespace Cwi.Coordination.Domain.Entities;

public sealed class PlacementMetric
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MetricsId { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public DateTimeOffset ObservedUtc { get; set; }
    public string? ScenarioId { get; set; }
    public PlacementRadioMetrics Radio { get; set; } = new();
    public PlacementEnvironmentMetrics Environment { get; set; } = new();
    public PlacementUncertaintyMetrics Uncertainty { get; set; } = new();
    public Dictionary<string, object?>? Metadata { get; set; }
}

public sealed class PlacementRadioMetrics
{
    public double LatencyMs { get; set; }
    public double? JitterMs { get; set; }
    public double PacketLoss { get; set; }
    public double RssiDbm { get; set; }
    public double SnrDb { get; set; }
    public double ThroughputMbps { get; set; }
    public double BandwidthMbps { get; set; }
    public double? NoiseFloorDbm { get; set; }
    public double? InterferenceIndex { get; set; }
}

public sealed class PlacementEnvironmentMetrics
{
    public double TemperatureC { get; set; }
    public double HumidityPct { get; set; }
    public double? WindSpeedMps { get; set; }
    public double? PrecipitationMm { get; set; }
    public string? Terrain { get; set; }
    public double? ObstructionIndex { get; set; }
}

public sealed class PlacementUncertaintyMetrics
{
    public double Confidence { get; set; }
    public double? RssiStddev { get; set; }
    public double? SnrStddev { get; set; }
    public double? LatencyStddev { get; set; }
    public double? ThroughputStddev { get; set; }
    public double? LocationStddevM { get; set; }
}
