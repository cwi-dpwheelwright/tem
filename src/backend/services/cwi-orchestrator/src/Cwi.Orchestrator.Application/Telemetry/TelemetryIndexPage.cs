using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Telemetry;

public sealed class TelemetryIndexPage
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("limit")]
    public int Limit { get; init; }

    [JsonPropertyName("items")]
    public IReadOnlyList<TelemetryIndexEntry> Items { get; init; } = Array.Empty<TelemetryIndexEntry>();
}
