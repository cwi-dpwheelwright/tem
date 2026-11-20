using System.Text.Json.Serialization;

namespace Cwi.Common.Events;

public sealed record EventEnvelope
{
    [JsonPropertyName("event_id")]
    public string EventId { get; init; } = Guid.NewGuid().ToString("n");

    [JsonPropertyName("timestamp_utc")]
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("service")]
    public string Service { get; init; } = "unknown";

    [JsonPropertyName("environment")]
    public string Environment { get; init; } = "unknown";

    [JsonPropertyName("type")]
    public string Type { get; init; } = "event";

    [JsonPropertyName("polarity")]
    public string Polarity { get; init; } = "positive";

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "info";

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string? TenantId { get; init; }

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; init; }

    [JsonPropertyName("data")]
    public Dictionary<string, object?> Data { get; init; } = new();
}
