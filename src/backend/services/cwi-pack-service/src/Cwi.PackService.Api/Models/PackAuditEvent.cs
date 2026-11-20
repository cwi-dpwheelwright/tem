using System.Text.Json.Serialization;

namespace Cwi.PackService.Api.Models;

public sealed class PackAuditEvent
{
    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("pack_id")]
    public string PackId { get; set; } = string.Empty;

    [JsonPropertyName("pack_version")]
    public string? PackVersion { get; set; }

    [JsonPropertyName("anchor")]
    public string? Anchor { get; set; }

    [JsonPropertyName("actor_id")]
    public string? ActorId { get; set; }

    [JsonPropertyName("tenant_id")]
    public string? TenantId { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("occurred_utc")]
    public DateTimeOffset OccurredUtc { get; set; }
}
