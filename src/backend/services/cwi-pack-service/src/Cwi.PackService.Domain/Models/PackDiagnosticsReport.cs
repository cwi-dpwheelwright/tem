using System.Text.Json.Serialization;

namespace Cwi.PackService.Domain.Models;

public sealed class PackDiagnosticsReport
{
    [JsonPropertyName("generated_utc")]
    public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("packs_root")]
    public string PacksRoot { get; set; } = string.Empty;

    [JsonPropertyName("packs_root_accessible")]
    public bool PacksRootAccessible { get; set; }

    [JsonPropertyName("index_path")]
    public string? IndexPath { get; set; }

    [JsonPropertyName("index_present")]
    public bool IndexPresent { get; set; }

    [JsonPropertyName("index_load_error")]
    public string? IndexLoadError { get; set; }

    [JsonPropertyName("directory_scan_error")]
    public string? DirectoryScanError { get; set; }

    [JsonPropertyName("indexed_pack_count")]
    public int IndexedPackCount { get; set; }

    [JsonPropertyName("discovered_pack_count")]
    public int DiscoveredPackCount { get; set; }

    [JsonPropertyName("available_pack_count")]
    public int AvailablePackCount { get; set; }

    [JsonPropertyName("filtered_pack_count")]
    public int FilteredPackCount { get; set; }

    [JsonPropertyName("unindexed_pack_count")]
    public int UnindexedPackCount { get; set; }

    [JsonPropertyName("packs")]
    public List<PackDiagnosticsEntry> Packs { get; set; } = new();
}
