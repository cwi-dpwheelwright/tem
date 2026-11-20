using System.Text.Json.Serialization;

namespace Cwi.Coordination.Application.Models;

public sealed class LedgerEventResponse
{
    [JsonPropertyName("event_id")]
    public Guid EventId { get; set; }

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("payload_json")]
    public string PayloadJson { get; set; } = string.Empty;

    [JsonPropertyName("prev_hash")]
    public string PrevHash { get; set; } = string.Empty;

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; set; }
}

public sealed class LedgerVerificationResponse
{
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("checked_events")]
    public int CheckedEvents { get; set; }

    [JsonPropertyName("last_valid_event_id")]
    public Guid? LastValidEventId { get; set; }

    [JsonPropertyName("failure_event_id")]
    public Guid? FailureEventId { get; set; }

    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; set; }

    [JsonPropertyName("checked_utc")]
    public DateTimeOffset CheckedUtc { get; set; }
}
