using System.Text.Json.Serialization;

namespace Cwi.Router.Api.Models;

public sealed class ReadyCheck
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = "down";

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    [JsonPropertyName("latency_ms")]
    public int? LatencyMs { get; init; }
}
