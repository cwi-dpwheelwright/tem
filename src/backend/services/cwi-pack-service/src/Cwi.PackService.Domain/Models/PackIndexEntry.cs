using System.Text.Json.Serialization;

namespace Cwi.PackService.Domain.Models;

public sealed class PackIndexEntry
{
    [JsonPropertyName("pack_id")]
    public string PackId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("latest")]
    public string? Latest { get; set; }

    [JsonPropertyName("versions")]
    public List<string> Versions { get; set; } = new();

    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }

    [JsonPropertyName("retired")]
    public bool? Retired { get; set; }

    [JsonPropertyName("retired_utc")]
    public DateTimeOffset? RetiredUtc { get; set; }
}
