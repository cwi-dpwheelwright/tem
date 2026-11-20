using System.Text.Json.Serialization;

namespace Cwi.PackService.Domain.Models;

public sealed class PackAvailabilitySummary
{
    [JsonPropertyName("generated_utc")]
    public DateTimeOffset GeneratedUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "empty";

    [JsonPropertyName("available_pack_count")]
    public int AvailablePackCount { get; set; }

    [JsonPropertyName("filtered_pack_count")]
    public int FilteredPackCount { get; set; }

    [JsonPropertyName("unindexed_pack_count")]
    public int UnindexedPackCount { get; set; }

    [JsonPropertyName("indexed_pack_count")]
    public int IndexedPackCount { get; set; }

    [JsonPropertyName("discovered_pack_count")]
    public int DiscoveredPackCount { get; set; }

    [JsonPropertyName("reasons")]
    public List<PackAvailabilityReason> Reasons { get; set; } = new();
}
