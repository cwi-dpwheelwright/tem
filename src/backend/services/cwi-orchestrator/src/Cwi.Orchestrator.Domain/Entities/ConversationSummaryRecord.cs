#nullable enable
namespace Cwi.Orchestrator.Domain.Entities;

public sealed class ConversationSummaryRecord
{
    public Guid Id { get; set; }
    public string SummaryId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ConversationId { get; set; } = "default";
    public string SummaryText { get; set; } = string.Empty;
    public string CoveredTurnIdsJson { get; set; } = "[]";
    public float Confidence { get; set; }
    public string ContentSha256 { get; set; } = string.Empty;
    public string? ProvenanceId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
#nullable disable
