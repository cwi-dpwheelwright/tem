#nullable enable
namespace Cwi.Orchestrator.Domain.Entities;

public sealed class OwnershipTransferRequest
{
    public Guid Id { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string FromUserId { get; set; } = string.Empty;
    public string ToUserId { get; set; } = string.Empty;
    public string TransferType { get; set; } = "transfer";
    public string? Reason { get; set; }
    public string Status { get; set; } = "pending";
    public int RequiredApprovals { get; set; } = 1;
    public string ApproversJson { get; set; } = "[]";
    public string ApprovedByJson { get; set; } = "[]";
    public string ApprovalTicketId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
}
#nullable disable
