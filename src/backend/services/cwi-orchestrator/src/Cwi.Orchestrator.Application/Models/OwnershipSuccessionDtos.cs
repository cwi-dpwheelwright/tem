#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class SuccessionPlanRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("asset_id")]
    public string AssetId { get; init; } = string.Empty;

    [JsonPropertyName("current_owner_id")]
    public string CurrentOwnerId { get; init; } = string.Empty;

    [JsonPropertyName("effective_utc")]
    public DateTimeOffset? EffectiveUtc { get; init; }

    [JsonPropertyName("successors")]
    public IReadOnlyList<SuccessionSuccessorDto> Successors { get; init; } = Array.Empty<SuccessionSuccessorDto>();

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public sealed class SuccessionSuccessorDto
{
    [JsonPropertyName("owner_id")]
    public string OwnerId { get; init; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; init; }

    [JsonPropertyName("trigger")]
    public string Trigger { get; init; } = string.Empty;

    [JsonPropertyName("approval_required")]
    public bool ApprovalRequired { get; init; }
}

public sealed class SuccessionPlanResponseDto
{
    [JsonPropertyName("plan_id")]
    public string PlanId { get; init; } = string.Empty;

    [JsonPropertyName("asset_id")]
    public string AssetId { get; init; } = string.Empty;

    [JsonPropertyName("current_owner_id")]
    public string CurrentOwnerId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("effective_utc")]
    public DateTimeOffset? EffectiveUtc { get; init; }

    [JsonPropertyName("successors")]
    public IReadOnlyList<SuccessionSuccessorDto> Successors { get; init; } = Array.Empty<SuccessionSuccessorDto>();
}

public sealed class SuccessionTriggerRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("plan_id")]
    public string PlanId { get; init; } = string.Empty;

    [JsonPropertyName("actor_id")]
    public string ActorId { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public sealed class SuccessionTriggerResponseDto
{
    [JsonPropertyName("plan_id")]
    public string PlanId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("transfer_request_id")]
    public string? TransferRequestId { get; init; }

    [JsonPropertyName("approval_ticket")]
    public ApprovalTicketDto? ApprovalTicket { get; init; }

    [JsonPropertyName("continuity_snapshot_id")]
    public string? ContinuitySnapshotId { get; init; }
}

public sealed class SuccessionRestoreRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("snapshot_id")]
    public string SnapshotId { get; init; } = string.Empty;

    [JsonPropertyName("actor_id")]
    public string ActorId { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public sealed class SuccessionRestoreResponseDto
{
    [JsonPropertyName("plan_id")]
    public string PlanId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("restored_utc")]
    public DateTimeOffset RestoredUtc { get; init; }
}

public sealed class OwnershipLineageBindingRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("asset_id")]
    public string AssetId { get; init; } = string.Empty;

    [JsonPropertyName("owner_id")]
    public string OwnerId { get; init; } = string.Empty;

    [JsonPropertyName("parent_asset_id")]
    public string? ParentAssetId { get; init; }

    [JsonPropertyName("relationship")]
    public string Relationship { get; init; } = "owner";

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public sealed class OwnershipLineageBindingResponseDto
{
    [JsonPropertyName("binding_id")]
    public string BindingId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; init; }
}
#nullable disable
