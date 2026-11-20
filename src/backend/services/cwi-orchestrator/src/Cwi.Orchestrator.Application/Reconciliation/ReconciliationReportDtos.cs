using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Reconciliation;

public sealed class ApprovalTicketSummaryDto
{
    [JsonPropertyName("ticket_id")]
    public string TicketId { get; init; } = string.Empty;

    [JsonPropertyName("risk")]
    public string Risk { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}

public sealed class ReconciliationReportDto
{
    [JsonPropertyName("record_id")]
    public string RecordId { get; init; } = string.Empty;

    [JsonPropertyName("canonical")]
    public Dictionary<string, object?> Canonical { get; init; } = new();

    [JsonPropertyName("conflicts")]
    public List<ConflictEntryDto> Conflicts { get; init; } = new();

    [JsonPropertyName("approval_ticket")]
    public ApprovalTicketSummaryDto? ApprovalTicket { get; init; }

    [JsonPropertyName("updated_utc")]
    public DateTimeOffset UpdatedUtc { get; init; }
}
