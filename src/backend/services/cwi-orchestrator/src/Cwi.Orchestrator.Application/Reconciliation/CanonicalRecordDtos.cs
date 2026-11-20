using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Reconciliation;

public sealed class SourceRecordDto
{
    [JsonPropertyName("source_id")]
    public string SourceId { get; init; } = string.Empty;

    [JsonPropertyName("updated_utc")]
    public DateTimeOffset UpdatedUtc { get; init; }

    [JsonPropertyName("attributes")]
    public Dictionary<string, object?> Attributes { get; init; } = new();
}

public sealed class CanonicalRecordUpsertRequestDto
{
    [JsonPropertyName("record_id")]
    public string RecordId { get; init; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("canonical")]
    public Dictionary<string, object?> Canonical { get; init; } = new();

    [JsonPropertyName("sources")]
    public List<SourceRecordDto> Sources { get; init; } = new();
}

public sealed class ConflictEntryDto
{
    [JsonPropertyName("field")]
    public string Field { get; init; } = string.Empty;

    [JsonPropertyName("values")]
    public List<string> Values { get; init; } = new();
}

public sealed class CanonicalRecordResponseDto
{
    [JsonPropertyName("record_id")]
    public string RecordId { get; init; } = string.Empty;

    [JsonPropertyName("canonical")]
    public Dictionary<string, object?> Canonical { get; init; } = new();

    [JsonPropertyName("sources")]
    public List<SourceRecordDto> Sources { get; init; } = new();

    [JsonPropertyName("conflicts")]
    public List<ConflictEntryDto> Conflicts { get; init; } = new();

    [JsonPropertyName("approval_ticket")]
    public ApprovalTicketSummaryDto? ApprovalTicket { get; init; }

    [JsonPropertyName("updated_utc")]
    public DateTimeOffset UpdatedUtc { get; init; }
}
