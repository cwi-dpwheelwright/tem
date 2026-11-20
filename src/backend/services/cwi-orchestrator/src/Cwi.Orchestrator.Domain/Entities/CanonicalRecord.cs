using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Domain.Entities;

public sealed class CanonicalRecord
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("record_id")]
    public string RecordId { get; set; } = string.Empty;

    [JsonPropertyName("canonical_json")]
    public string CanonicalJson { get; set; } = string.Empty;

    [JsonPropertyName("sources_json")]
    public string SourcesJson { get; set; } = string.Empty;

    [JsonPropertyName("conflicts_json")]
    public string ConflictsJson { get; set; } = string.Empty;

    [JsonPropertyName("updated_utc")]
    public DateTimeOffset UpdatedUtc { get; set; }
}
