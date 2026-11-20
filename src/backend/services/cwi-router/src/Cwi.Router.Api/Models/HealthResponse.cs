using System.Text.Json.Serialization;

namespace Cwi.Router.Api.Models;

public sealed class HealthResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("services")]
    public IReadOnlyDictionary<string, string> Services { get; init; } = new Dictionary<string, string>();
}
