#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class TelemetryWindowDto
{
    [JsonPropertyName("start")]
    public DateTimeOffset Start { get; init; }

    [JsonPropertyName("end")]
    public DateTimeOffset End { get; init; }
}

public sealed class TelemetrySignalDto
{
    [JsonPropertyName("tag")]
    public string Tag { get; init; } = string.Empty;

    [JsonPropertyName("unit")]
    public string Unit { get; init; } = string.Empty;

    [JsonPropertyName("series")]
    public List<float> Series { get; init; } = new();
}

public sealed class TelemetryIngestRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("window")]
    public TelemetryWindowDto Window { get; init; } = new();

    [JsonPropertyName("signals")]
    public List<TelemetrySignalDto> Signals { get; init; } = new();

    [JsonPropertyName("metadata")]
    public Dictionary<string, object?> Metadata { get; init; } = new();
}

public sealed class TelemetryIngestResponseDto
{
    [JsonPropertyName("accepted")]
    public bool Accepted { get; init; }

    [JsonPropertyName("ingest_id")]
    public string IngestId { get; init; } = string.Empty;

    [JsonPropertyName("trace")]
    public TracePayload Trace { get; init; } = new();
}
#nullable disable
