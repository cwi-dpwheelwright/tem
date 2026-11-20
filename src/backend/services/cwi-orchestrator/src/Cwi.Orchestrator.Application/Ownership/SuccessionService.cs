#nullable enable
using System.Text.Json;
using System.Linq;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Ownership;

public sealed class SuccessionService : DomainServiceBase
{
    private readonly ISuccessionPlanRepository _repository;
    private readonly ITenantConfigRepository _tenantConfigRepository;
    private readonly OwnershipTransferService _transferService;
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;

    public SuccessionService(
        ISuccessionPlanRepository repository,
        ITenantConfigRepository tenantConfigRepository,
        OwnershipTransferService transferService,
        ILedgerRepository ledgerRepository,
        LedgerEventFactory ledgerEventFactory)
    {
        _repository = repository;
        _tenantConfigRepository = tenantConfigRepository;
        _transferService = transferService;
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
    }

    public Task<ServiceResponse<SuccessionPlanResponseDto>> CreatePlanAsync(
        SuccessionPlanRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleCreateAsync(request, cancellationToken));
    }

    public Task<ServiceResponse<SuccessionTriggerResponseDto>> TriggerAsync(
        SuccessionTriggerRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleTriggerAsync(request, cancellationToken));
    }

    public Task<ServiceResponse<SuccessionRestoreResponseDto>> RestoreAsync(
        SuccessionRestoreRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleRestoreAsync(request, cancellationToken));
    }

    private async Task<ServiceResponse<SuccessionPlanResponseDto>> HandleCreateAsync(
        SuccessionPlanRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId)
            || string.IsNullOrWhiteSpace(request.AssetId)
            || string.IsNullOrWhiteSpace(request.CurrentOwnerId))
        {
            return ServiceResponse<SuccessionPlanResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id, asset_id, and current_owner_id required"),
                statusCode: 400,
                message: "missing required fields");
        }

        if (request.Successors is null || request.Successors.Count == 0)
        {
            return ServiceResponse<SuccessionPlanResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "successors required"),
                statusCode: 400,
                message: "successors required");
        }

        var successors = request.Successors
            .OrderBy(s => s.Order)
            .Select(s => new SuccessionSuccessorDto
            {
                OwnerId = s.OwnerId.Trim(),
                Order = s.Order,
                Trigger = s.Trigger.Trim(),
                ApprovalRequired = s.ApprovalRequired
            })
            .ToList();

        var now = DateTimeOffset.UtcNow;
        var plan = new SuccessionPlan
        {
            Id = Guid.NewGuid(),
            PlanId = Guid.NewGuid().ToString("N"),
            TenantId = request.TenantId.Trim(),
            AssetId = request.AssetId.Trim(),
            CurrentOwnerId = request.CurrentOwnerId.Trim(),
            Status = "active",
            SuccessorsJson = JsonSerializer.Serialize(successors),
            EffectiveUtc = request.EffectiveUtc,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        await _repository.AddAsync(plan, cancellationToken).ConfigureAwait(false);

        var response = new SuccessionPlanResponseDto
        {
            PlanId = plan.PlanId,
            AssetId = plan.AssetId,
            CurrentOwnerId = plan.CurrentOwnerId,
            Status = plan.Status,
            EffectiveUtc = plan.EffectiveUtc,
            Successors = successors
        };
        return ServiceResponse<SuccessionPlanResponseDto>.Success(response);
    }

    private async Task<ServiceResponse<SuccessionTriggerResponseDto>> HandleTriggerAsync(
        SuccessionTriggerRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId)
            || string.IsNullOrWhiteSpace(request.PlanId)
            || string.IsNullOrWhiteSpace(request.ActorId))
        {
            return ServiceResponse<SuccessionTriggerResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id, plan_id, and actor_id required"),
                statusCode: 400,
                message: "tenant_id, plan_id, and actor_id required");
        }

        var plan = await _repository
            .GetAsync(request.TenantId.Trim(), request.PlanId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (plan is null)
        {
            return ServiceResponse<SuccessionTriggerResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "plan not found"),
                statusCode: 404,
                message: "plan not found");
        }

        var successors = DeserializeSuccessors(plan.SuccessorsJson);
        if (successors is null)
        {
            return ServiceResponse<SuccessionTriggerResponseDto>.Fail(
                new DomainError(ErrorCodes.InternalError, "succession plan successors invalid"),
                statusCode: 500,
                message: "succession plan successors invalid");
        }

        var successor = successors.OrderBy(s => s.Order).FirstOrDefault();
        if (successor is null)
        {
            return ServiceResponse<SuccessionTriggerResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "no successor defined"),
                statusCode: 400,
                message: "no successor defined");
        }

        var approversResult = await ResolveApproverIdsAsync(plan.TenantId, request.ActorId, cancellationToken)
            .ConfigureAwait(false);
        if (!approversResult.IsSuccess || approversResult.Data is null)
        {
            return ServiceResponse<SuccessionTriggerResponseDto>.Fail(
                approversResult.Error ?? new DomainError(ErrorCodes.InternalError, "approver resolution failed"),
                statusCode: approversResult.StatusCode,
                message: approversResult.Message);
        }

        var approvers = approversResult.Data
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (approvers.Count == 0)
        {
            approvers.Add(request.ActorId);
        }

        if (!successor.ApprovalRequired && !approvers.Contains(request.ActorId, StringComparer.OrdinalIgnoreCase))
        {
            approvers.Add(request.ActorId);
        }

        var transferRequest = new OwnershipTransferRequestDto
        {
            TenantId = plan.TenantId,
            FromUserId = plan.CurrentOwnerId,
            ToUserId = successor.OwnerId,
            TransferType = "transfer",
            Reason = $"succession_plan:{plan.PlanId};{request.Reason}",
            RequiredApprovals = 1,
            ApproverIds = approvers
        };

        var transferResult = await _transferService.CreateAsync(transferRequest, cancellationToken).ConfigureAwait(false);
        if (!transferResult.IsSuccess || transferResult.Data is null)
        {
            return ServiceResponse<SuccessionTriggerResponseDto>.Fail(
                transferResult.Error ?? new DomainError(ErrorCodes.InternalError, "transfer failed"),
                statusCode: transferResult.StatusCode,
                message: transferResult.Message);
        }

        if (!successor.ApprovalRequired)
        {
            await _transferService
                .ApplyApprovalDecisionAsync(
                    transferResult.Data.ApprovalTicket?.TicketId ?? string.Empty,
                    "approve",
                    request.ActorId,
                    request.Reason,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var snapshotId = $"snap-{plan.PlanId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var snapshotPayload = new SuccessionSnapshotPayload
        {
            PlanId = plan.PlanId,
            TenantId = plan.TenantId,
            AssetId = plan.AssetId,
            CurrentOwnerId = plan.CurrentOwnerId,
            Status = plan.Status,
            EffectiveUtc = plan.EffectiveUtc,
            Successors = successors,
            CreatedUtc = plan.CreatedUtc,
            UpdatedUtc = plan.UpdatedUtc
        };
        var snapshot = new SuccessionSnapshot
        {
            Id = Guid.NewGuid(),
            SnapshotId = snapshotId,
            TenantId = plan.TenantId,
            PlanId = plan.PlanId,
            SnapshotJson = JsonSerializer.Serialize(snapshotPayload),
            CreatedUtc = DateTimeOffset.UtcNow
        };
        await _repository.AddSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);

        await AppendLedgerEventAsync(plan, successor, request.ActorId, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<SuccessionTriggerResponseDto>.Success(new SuccessionTriggerResponseDto
        {
            PlanId = plan.PlanId,
            Status = successor.ApprovalRequired ? "pending_approval" : "executed",
            TransferRequestId = transferResult.Data.RequestId,
            ApprovalTicket = transferResult.Data.ApprovalTicket,
            ContinuitySnapshotId = snapshotId
        });
    }

    private async Task<ServiceResponse<SuccessionRestoreResponseDto>> HandleRestoreAsync(
        SuccessionRestoreRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId)
            || string.IsNullOrWhiteSpace(request.SnapshotId)
            || string.IsNullOrWhiteSpace(request.ActorId))
        {
            return ServiceResponse<SuccessionRestoreResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id, snapshot_id, and actor_id required"),
                statusCode: 400,
                message: "tenant_id, snapshot_id, and actor_id required");
        }

        var snapshot = await _repository
            .GetSnapshotAsync(request.TenantId.Trim(), request.SnapshotId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        if (snapshot is null)
        {
            return ServiceResponse<SuccessionRestoreResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "snapshot not found"),
                statusCode: 404,
                message: "snapshot not found");
        }

        var payload = DeserializeSnapshot(snapshot.SnapshotJson);
        if (payload is null)
        {
            return ServiceResponse<SuccessionRestoreResponseDto>.Fail(
                new DomainError(ErrorCodes.InternalError, "snapshot payload invalid"),
                statusCode: 500,
                message: "snapshot payload invalid");
        }

        var existingPlan = await _repository
            .GetAsync(request.TenantId.Trim(), payload.PlanId, cancellationToken)
            .ConfigureAwait(false);

        var plan = existingPlan ?? new SuccessionPlan
        {
            Id = Guid.NewGuid(),
            PlanId = payload.PlanId,
            TenantId = payload.TenantId,
            AssetId = payload.AssetId,
            CurrentOwnerId = payload.CurrentOwnerId,
            Status = payload.Status,
            SuccessorsJson = JsonSerializer.Serialize(payload.Successors),
            EffectiveUtc = payload.EffectiveUtc,
            CreatedUtc = payload.CreatedUtc,
            UpdatedUtc = payload.UpdatedUtc
        };

        if (existingPlan is null)
        {
            await _repository.AddAsync(plan, cancellationToken).ConfigureAwait(false);
        }

        await AppendRestoreLedgerEventAsync(plan, request.ActorId, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<SuccessionRestoreResponseDto>.Success(new SuccessionRestoreResponseDto
        {
            PlanId = plan.PlanId,
            Status = "restored",
            RestoredUtc = DateTimeOffset.UtcNow
        });
    }

    private async Task AppendLedgerEventAsync(
        SuccessionPlan plan,
        SuccessionSuccessorDto successor,
        string actorId,
        CancellationToken cancellationToken)
    {
        var traceId = $"succession:{plan.TenantId}:{plan.PlanId}";
        var prevHash = await _ledgerRepository.GetLatestHashAsync(traceId, cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(new
        {
            tenant_id = plan.TenantId,
            plan_id = plan.PlanId,
            asset_id = plan.AssetId,
            from_owner_id = plan.CurrentOwnerId,
            to_owner_id = successor.OwnerId,
            actor_id = actorId
        });

        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId,
            "ownership.succession_triggered",
            payload,
            prevHash ?? string.Empty);

        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendRestoreLedgerEventAsync(
        SuccessionPlan plan,
        string actorId,
        CancellationToken cancellationToken)
    {
        var traceId = $"succession:{plan.TenantId}:{plan.PlanId}";
        var prevHash = await _ledgerRepository.GetLatestHashAsync(traceId, cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(new
        {
            tenant_id = plan.TenantId,
            plan_id = plan.PlanId,
            asset_id = plan.AssetId,
            actor_id = actorId,
            action = "restore"
        });

        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId,
            "ownership.succession_restored",
            payload,
            prevHash ?? string.Empty);

        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ServiceResponse<IReadOnlyList<string>>> ResolveApproverIdsAsync(
        string tenantId,
        string actorId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ServiceResponse<IReadOnlyList<string>>.Success(new List<string> { actorId });
        }

        var config = await _tenantConfigRepository.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (config is null || string.IsNullOrWhiteSpace(config.ConfigJson))
        {
            return ServiceResponse<IReadOnlyList<string>>.Success(new List<string> { actorId });
        }

        try
        {
            using var document = JsonDocument.Parse(config.ConfigJson);
            var approvers = ExtractApproverIds(document.RootElement);
            if (approvers.Count == 0)
            {
                approvers.Add(actorId);
            }

            return ServiceResponse<IReadOnlyList<string>>.Success(approvers);
        }
        catch (JsonException)
        {
            return ServiceResponse<IReadOnlyList<string>>.Fail(
                new DomainError(ErrorCodes.InternalError, "Tenant succession approvers invalid."),
                statusCode: 500,
                message: "Tenant succession approvers invalid");
        }
    }

    private static List<string> ExtractApproverIds(JsonElement root)
    {
        if (TryGetStringArray(root, out var approvers, "succession_approvers"))
        {
            return approvers;
        }

        if (TryGetStringArray(root, out approvers, "ownership_succession_approvers"))
        {
            return approvers;
        }

        if (TryGetStringArray(root, out approvers, "succession", "approvers"))
        {
            return approvers;
        }

        if (TryGetStringArray(root, out approvers, "ownership", "succession_approvers"))
        {
            return approvers;
        }

        if (TryGetStringArray(root, out approvers, "ownership", "succession", "approvers"))
        {
            return approvers;
        }

        return new List<string>();
    }

    private static bool TryGetStringArray(JsonElement root, out List<string> values, params string[] path)
    {
        values = new List<string>();
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out var next))
            {
                return false;
            }

            current = next;
        }

        if (current.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Expected array.");
        }

        foreach (var item in current.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("Expected string in array.");
            }

            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }

        return true;
    }

    private static List<SuccessionSuccessorDto>? DeserializeSuccessors(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<SuccessionSuccessorDto>();
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<SuccessionSuccessorDto>>(json);
            return list ?? new List<SuccessionSuccessorDto>();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SuccessionSnapshotPayload? DeserializeSnapshot(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SuccessionSnapshotPayload>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class SuccessionSnapshotPayload
    {
        public string PlanId { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public string AssetId { get; set; } = string.Empty;
        public string CurrentOwnerId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset? EffectiveUtc { get; set; }
        public List<SuccessionSuccessorDto> Successors { get; set; } = new();
        public DateTimeOffset CreatedUtc { get; set; }
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
#nullable disable
