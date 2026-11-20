using System.Text.Json.Serialization;

namespace Cwi.PackService.Domain.Models;

public sealed class PackDiagnosticsEntry
{
    [JsonPropertyName("pack_id")]
    public string PackId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("latest")]
    public string? Latest { get; set; }

    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }

    [JsonPropertyName("retired")]
    public bool? Retired { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "filtered";

    [JsonPropertyName("index_entry_present")]
    public bool IndexEntryPresent { get; set; }

    [JsonPropertyName("directory_present")]
    public bool DirectoryPresent { get; set; }

    [JsonPropertyName("dependency_graph_available")]
    public bool DependencyGraphAvailable { get; set; }

    [JsonPropertyName("schema_valid")]
    public bool? SchemaValid { get; set; }

    [JsonPropertyName("root_pack_json_exists")]
    public bool RootPackJsonExists { get; set; }

    [JsonPropertyName("root_anchor_exists")]
    public bool RootAnchorExists { get; set; }

    [JsonPropertyName("latest_pack_json_exists")]
    public bool LatestPackJsonExists { get; set; }

    [JsonPropertyName("latest_anchor_exists")]
    public bool LatestAnchorExists { get; set; }

    [JsonPropertyName("dependencies")]
    public List<string> Dependencies { get; set; } = new();

    [JsonPropertyName("issues")]
    public List<PackDiagnosticsIssue> Issues { get; set; } = new();
}
