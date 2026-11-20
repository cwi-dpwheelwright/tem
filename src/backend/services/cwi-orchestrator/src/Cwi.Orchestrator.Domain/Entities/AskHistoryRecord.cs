#nullable enable
namespace Cwi.Orchestrator.Domain.Entities;

public sealed class AskHistoryRecord
{
    public Guid Id { get; set; }
    public string? RequestId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ConversationId { get; set; } = "default";
    public string TurnId { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string? TraceId { get; set; }
    public string? ProvenanceId { get; set; }
    public string? PackName { get; set; }
    public string? PackHash { get; set; }
    public string ContentSha256 { get; set; } = string.Empty;
    public string SummaryStatus { get; set; } = "pending";
    public string? TraceJson { get; set; }
    public string? AttachmentsJson { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
#nullable disable
