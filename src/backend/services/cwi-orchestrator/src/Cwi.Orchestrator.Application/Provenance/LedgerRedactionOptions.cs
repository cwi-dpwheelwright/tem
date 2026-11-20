using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Provenance;

public sealed class LedgerRedactionOptions
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("fields")]
    public IReadOnlyList<string> Fields { get; init; } = Array.Empty<string>();
}
