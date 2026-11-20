using System.Text.Json.Serialization;

namespace Cwi.Coordination.Application.Models;

public sealed class DegradationEventRequest
{
    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("observed_utc")]
    public DateTimeOffset ObservedUtc { get; set; }

    [JsonPropertyName("severity")]
    public double Severity { get; set; }

    [JsonPropertyName("details")]
    public Dictionary<string, object?> Details { get; set; } = new();

    [JsonPropertyName("provenance")]
    public Dictionary<string, object?>? Provenance { get; set; }
}

public sealed class DegradationEventReceipt
{
    [JsonPropertyName("event_id")]
    public string EventId { get; set; } = string.Empty;

    [JsonPropertyName("received_utc")]
    public DateTimeOffset ReceivedUtc { get; set; }
}
