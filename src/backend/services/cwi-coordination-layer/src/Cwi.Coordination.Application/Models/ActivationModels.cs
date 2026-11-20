using System.Text.Json.Serialization;

namespace Cwi.Coordination.Application.Models;

public sealed class ActivationStatusResponse
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("revoked_utc")]
    public DateTimeOffset? RevokedUtc { get; set; }

    [JsonPropertyName("checked_utc")]
    public DateTimeOffset CheckedUtc { get; set; }
}
