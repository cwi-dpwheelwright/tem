using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Provenance;

public sealed class LedgerTombstoneRequestDto
{
    [JsonPropertyName("trace_id")]
    public string TraceId { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}
