using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Telemetry;

public sealed class TelemetryIndexOptions
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("max_entries")]
    public int MaxEntries { get; init; } = 1000;
}
