using System.Text.Json.Serialization;

namespace Cwi.Coordination.Application.Models;

public sealed class RoundCreateRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class RoundResponse
{
    [JsonPropertyName("round_id")]
    public Guid RoundId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

public sealed class HeartbeatRequest
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}
