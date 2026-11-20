using System.Text.Json.Serialization;

namespace Cwi.PackService.Domain.Models;

public sealed class PackIndex
{
    [JsonPropertyName("packs")]
    public List<PackIndexEntry> Packs { get; set; } = new();
}
