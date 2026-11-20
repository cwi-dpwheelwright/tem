#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class OwnershipTransferRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("from_user_id")]
    public string FromUserId { get; init; } = string.Empty;

    [JsonPropertyName("to_user_id")]
    public string ToUserId { get; init; } = string.Empty;

    [JsonPropertyName("transfer_type")]
    public string TransferType { get; init; } = "transfer";

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("required_approvals")]
    public int RequiredApprovals { get; init; } = 1;

    [JsonPropertyName("approver_ids")]
    public IReadOnlyList<string> ApproverIds { get; init; } = Array.Empty<string>();
}

public sealed class OwnershipTransferResponseDto
{
    [JsonPropertyName("approval_ticket")]
    public ApprovalTicketDto? ApprovalTicket { get; init; }

    [JsonPropertyName("request_id")]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("transfer_type")]
    public string TransferType { get; init; } = string.Empty;

    [JsonPropertyName("required_approvals")]
    public int RequiredApprovals { get; init; }

    [JsonPropertyName("approver_ids")]
    public IReadOnlyList<string> ApproverIds { get; init; } = Array.Empty<string>();

    [JsonPropertyName("approved_by")]
    public IReadOnlyList<string> ApprovedBy { get; init; } = Array.Empty<string>();

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; init; }

    [JsonPropertyName("updated_utc")]
    public DateTimeOffset UpdatedUtc { get; init; }

    [JsonPropertyName("completed_utc")]
    public DateTimeOffset? CompletedUtc { get; init; }
}

public sealed class OwnershipTransferDecisionRequestDto
{
    [JsonPropertyName("approver_id")]
    public string ApproverId { get; init; } = string.Empty;

    [JsonPropertyName("decision")]
    public string Decision { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public sealed class OwnershipTransferRevokeRequestDto
{
    [JsonPropertyName("request_id")]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("actor_id")]
    public string ActorId { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
#nullable disable
