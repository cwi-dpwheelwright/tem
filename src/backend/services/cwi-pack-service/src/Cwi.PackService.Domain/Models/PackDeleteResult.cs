using System.Text.Json.Serialization;

namespace Cwi.PackService.Domain.Models;

public sealed class PackDeleteResult
{
    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }

    [JsonPropertyName("pack_id")]
    public string PackId { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
