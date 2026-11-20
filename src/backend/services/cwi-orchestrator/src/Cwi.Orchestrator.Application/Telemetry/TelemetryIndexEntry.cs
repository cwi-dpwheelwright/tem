using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Telemetry;

public sealed class TelemetryIndexEntry
{
    [JsonPropertyName("ingest_id")]
    public string IngestId { get; init; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("window_start_utc")]
    public DateTimeOffset WindowStartUtc { get; init; }

    [JsonPropertyName("window_end_utc")]
    public DateTimeOffset WindowEndUtc { get; init; }

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; init; }
}
