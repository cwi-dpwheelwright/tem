using System.Text.Json.Serialization;

namespace Cwi.Router.Api.Models;

public sealed class ReadyResponse
{
    [JsonPropertyName("ready")]
    public bool Ready { get; init; }

    [JsonPropertyName("checks")]
    public IReadOnlyDictionary<string, ReadyCheck> Checks { get; init; } = new Dictionary<string, ReadyCheck>();

    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}
