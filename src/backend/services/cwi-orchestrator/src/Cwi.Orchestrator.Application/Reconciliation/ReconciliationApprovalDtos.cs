using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Reconciliation;

public sealed class ReconciliationApprovalCompleteResponseDto
{
    [JsonPropertyName("record_id")]
    public string RecordId { get; init; } = string.Empty;

    [JsonPropertyName("ticket_id")]
    public string TicketId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("decision")]
    public string Decision { get; init; } = string.Empty;

    [JsonPropertyName("conflicts_count")]
    public int ConflictsCount { get; init; }

    [JsonPropertyName("updated_utc")]
    public DateTimeOffset UpdatedUtc { get; init; }
}
