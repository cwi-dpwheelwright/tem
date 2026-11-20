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

public sealed class OwnershipTransferService : DomainServiceBase
{
    private const string EventRequested = "OWNERSHIP_TRANSFER_REQUESTED";
    private const string EventApproved = "OWNERSHIP_TRANSFER_APPROVED";
    private const string EventDenied = "OWNERSHIP_TRANSFER_DENIED";
    private const string EventExecuted = "OWNERSHIP_TRANSFER_EXECUTED";
    private const string EventRevoked = "OWNERSHIP_TRANSFER_REVOKED";

    private readonly IOwnershipTransferRepository _repository;
    private readonly IApprovalTicketRepository _ticketRepository;
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;
    private readonly IOwnershipPolicyEvaluator _policyEvaluator;
    private readonly Telemetry.OrchestratorMetrics _metrics;

    public OwnershipTransferService(
        IOwnershipTransferRepository repository,
        IApprovalTicketRepository ticketRepository,
        ILedgerRepository ledgerRepository,
        LedgerEventFactory ledgerEventFactory,
        IOwnershipPolicyEvaluator policyEvaluator,
        Telemetry.OrchestratorMetrics metrics)
    {
        _repository = repository;
        _ticketRepository = ticketRepository;
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
        _policyEvaluator = policyEvaluator;
        _metrics = metrics;
    }

    public Task<ServiceResponse<OwnershipTransferResponseDto>> CreateAsync(
        OwnershipTransferRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleCreateAsync(request, cancellationToken));
    }

    public Task<ServiceResponse<OwnershipTransferResponseDto>> GetAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleGetAsync(requestId, cancellationToken));
    }

    public Task<ServiceResponse<OwnershipTransferResponseDto>> DecideAsync(
        string requestId,
        OwnershipTransferDecisionRequestDto decision,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleDecisionAsync(requestId, decision, cancellationToken));
    }

    public Task<ServiceResponse<OwnershipTransferResponseDto>> RevokeAsync(
        OwnershipTransferRevokeRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleRevokeAsync(request, cancellationToken));
    }

    public Task ApplyApprovalDecisionAsync(
        string ticketId,
        string decision,
        string approverId,
        string? reason,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            await HandleApprovalAsync(ticketId, decision, approverId, reason, cancellationToken).ConfigureAwait(false);
            return ServiceResponse<bool>.Success(true);
        });
    }

    private async Task<ServiceResponse<OwnershipTransferResponseDto>> HandleCreateAsync(
        OwnershipTransferRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId)
            || string.IsNullOrWhiteSpace(request.FromUserId)
            || string.IsNullOrWhiteSpace(request.ToUserId))
        {
            return ServiceResponse<OwnershipTransferResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id, from_user_id, and to_user_id are required."),
                statusCode: 400,
                message: "Missing required fields");
        }

        if (request.RequiredApprovals < 1)
        {
            return ServiceResponse<OwnershipTransferResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "required_approvals must be at least 1."),
                statusCode: 400,
                message: "Invalid required approvals");
        }

        var transferType = string.IsNullOrWhiteSpace(request.TransferType)
            ? "transfer"
            : request.TransferType.Trim().ToLowerInvariant();

        if (!IsSupportedTransferType(transferType))
        {
            return ServiceResponse<OwnershipTransferResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "transfer_type must be transfer or delegate."),
                statusCode: 400,
                message: "Invalid transfer type");
        }

        var policyDecision = _policyEvaluator.EvaluateRequest(request, transferType);
        if (!policyDecision.Allowed)
        {
            var reason = policyDecision.Reason ?? "Ownership transfer not permitted.";
            return ServiceResponse<OwnershipTransferResponseDto>.Fail(
                new DomainError(ErrorCodes.Forbidden, reason),
                statusCode: 403,
                message: reason);
        }

        var approvers = request.ApproverIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (approvers.Count < request.RequiredApprovals)
        {
            return ServiceResponse<OwnershipTransferResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "approver_ids must satisfy required_approvals."),
                statusCode: 400,
                message: "Insufficient approvers");
        }

        var idempotencyKey = $"{request.TenantId}:{request.FromUserId}:{request.ToUserId}:{transferType}";
        var existing = await _repository
            .GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            var existingTicket = await _ticketRepository
                .GetByIdempotencyKeyAsync(existing.IdempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            return ServiceResponse<OwnershipTransferResponseDto>.Success(ToResponse(existing, existingTicket));
        }

        var now = DateTimeOffset.UtcNow;
        var ticketId = Guid.NewGuid().ToString("N");
        var ticket = new ApprovalTicket
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            TenantId = request.TenantId.Trim(),
            UserId = request.FromUserId.Trim(),
            Risk = "high",
            Reason = request.Reason?.Trim() ?? "Ownership transfer requires approval.",
            Status = "pending",
            IdempotencyKey = idempotencyKey,
            CreatedUtc = now,
            UpdatedUtc = now
        };
        await _ticketRepository.AddAsync(ticket, cancellationToken).ConfigureAwait(false);

        var entity = new OwnershipTransferRequest
        {
            Id = Guid.NewGuid(),
            RequestId = Guid.NewGuid().ToString("N"),
            TenantId = request.TenantId.Trim(),
            FromUserId = request.FromUserId.Trim(),
            ToUserId = request.ToUserId.Trim(),
            TransferType = transferType,
            Reason = request.Reason?.Trim(),
            Status = "pending",
            RequiredApprovals = request.RequiredApprovals,
            ApproversJson = JsonSerializer.Serialize(approvers),
            ApprovedByJson = "[]",
            IdempotencyKey = idempotencyKey,
            ApprovalTicketId = ticketId,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        await _repository.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        await RecordLedgerEventAsync(entity, EventRequested, cancellationToken, actorId: request.FromUserId).ConfigureAwait(false);
        _metrics.RecordOwnershipTransferRequested(transferType);

        return ServiceResponse<OwnershipTransferResponseDto>.Success(ToResponse(entity, ticket));
    }

    private async Task<ServiceResponse<OwnershipTransferResponseDto>> HandleGetAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return ServiceResponse<OwnershipTransferResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "request_id is required."),
                statusCode: 400,
                message: "request_id required");
        }

        var entity = await _repository.GetByRequestIdAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return ServiceResponse<OwnershipTransferResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "request not found."),
                statusCode: 404,
                message: "request not found");
        }

        ApprovalTicket? ticket = null;
        if (!string.IsNullOrWhiteSpace(entity.ApprovalTicketId))
        {
            ticket = await _ticketRepository
                .GetByTicketIdAsync(entity.ApprovalTicketId, cancellationToken)
                .ConfigureAwait(false);
        }

        return ServiceResponse<OwnershipTransferResponseDto>.Success(ToResponse(entity, ticket));
    }

    private Task<ServiceResponse<OwnershipTransferResponseDto>> HandleDecisionAsync(
        string requestId,
        OwnershipTransferDecisionRequestDto decision,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ServiceResponse<OwnershipTransferResponseDto>.Fail(
            new DomainError(ErrorCodes.InvalidRequest, "use /api/approval/complete for ownership approvals."),
            statusCode: 400,
            message: "approval flow moved"));
    }

    private async Task<ServiceResponse<OwnershipTransferResponseDto>> HandleRevokeAsync(
        OwnershipTransferRevokeRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId) || string.IsNullOrWhiteSpace(request.ActorId))
        {
            return ServiceResponse<OwnershipTransferResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "request_id and actor_id are required."),
                statusCode: 400,
                message: "request_id and actor_id required");
        }

        var entity = await _repository.GetByRequestIdAsync(request.RequestId, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return ServiceResponse<OwnershipTransferResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "request not found."),
                statusCode: 404,
                message: "request not found");
        }

        var policyDecision = _policyEvaluator.EvaluateRevoke(entity, request.ActorId);
        if (!policyDecision.Allowed)
        {
            var reason = policyDecision.Reason ?? "Ownership revoke not permitted.";
            return ServiceResponse<OwnershipTransferResponseDto>.Fail(
                new DomainError(ErrorCodes.Forbidden, reason),
                statusCode: 403,
                message: reason);
        }

        if (!IsRevocable(entity.Status))
        {
            return ServiceResponse<OwnershipTransferResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "request cannot be revoked in current state."),
                statusCode: 409,
                message: "request cannot be revoked");
        }

        entity.Status = "revoked";
        entity.CompletedUtc = DateTimeOffset.UtcNow;
        entity.UpdatedUtc = entity.CompletedUtc.Value;
        await _repository.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(entity.ApprovalTicketId))
        {
            var ticket = await _ticketRepository
                .GetByTicketIdAsync(entity.ApprovalTicketId, cancellationToken)
                .ConfigureAwait(false);
            if (ticket is not null)
            {
                ticket.Status = "revoked";
                ticket.UpdatedUtc = DateTimeOffset.UtcNow;
                await _ticketRepository.UpdateAsync(ticket, cancellationToken).ConfigureAwait(false);
            }
        }

        await RecordLedgerEventAsync(entity, EventRevoked, cancellationToken, request.Reason, request.ActorId)
            .ConfigureAwait(false);
        _metrics.RecordOwnershipTransferRevoked(entity.TransferType);

        ApprovalTicket? approvalTicket = null;
        if (!string.IsNullOrWhiteSpace(entity.ApprovalTicketId))
        {
            approvalTicket = await _ticketRepository
                .GetByTicketIdAsync(entity.ApprovalTicketId, cancellationToken)
                .ConfigureAwait(false);
        }

        return ServiceResponse<OwnershipTransferResponseDto>.Success(ToResponse(entity, approvalTicket));
    }

    private async Task HandleApprovalAsync(
        string ticketId,
        string decision,
        string approverId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var entity = await _repository.GetByApprovalTicketIdAsync(ticketId, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return;
        }

        if (!string.Equals(entity.Status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var approvers = DeserializeList(entity.ApproversJson);
        if (!approvers.Contains(approverId, StringComparer.OrdinalIgnoreCase))
        {
            await RecordLedgerEventAsync(entity, EventDenied, cancellationToken, reason, approverId)
                .ConfigureAwait(false);
            return;
        }

        var normalizedDecision = decision.Trim().ToLowerInvariant();
        if (normalizedDecision == "deny")
        {
            entity.Status = "denied";
            entity.CompletedUtc = DateTimeOffset.UtcNow;
            entity.UpdatedUtc = entity.CompletedUtc.Value;
            await _repository.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);
            await RecordLedgerEventAsync(entity, EventDenied, cancellationToken, reason, approverId)
                .ConfigureAwait(false);
            return;
        }

        if (normalizedDecision != "approve")
        {
            return;
        }

        var approvedBy = DeserializeList(entity.ApprovedByJson);
        if (!approvedBy.Contains(approverId, StringComparer.OrdinalIgnoreCase))
        {
            approvedBy.Add(approverId);
        }

        entity.ApprovedByJson = JsonSerializer.Serialize(approvedBy);
        entity.UpdatedUtc = DateTimeOffset.UtcNow;

        if (approvedBy.Count >= entity.RequiredApprovals)
        {
            entity.Status = "approved";
            entity.CompletedUtc = DateTimeOffset.UtcNow;
            await _repository.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);
            await RecordLedgerEventAsync(entity, EventApproved, cancellationToken, reason, approverId)
                .ConfigureAwait(false);
            await RecordLedgerEventAsync(entity, EventExecuted, cancellationToken, reason, approverId)
                .ConfigureAwait(false);
            _metrics.RecordOwnershipTransferApproved(entity.TransferType);
            return;
        }

        await _repository.UpdateAsync(entity, cancellationToken).ConfigureAwait(false);
        await RecordLedgerEventAsync(entity, EventApproved, cancellationToken, reason, approverId)
            .ConfigureAwait(false);
    }

    private static OwnershipTransferResponseDto ToResponse(OwnershipTransferRequest entity, ApprovalTicket? ticket)
    {
        return new OwnershipTransferResponseDto
        {
            ApprovalTicket = ticket is null ? null : new ApprovalTicketDto
            {
                TicketId = ticket.TicketId,
                Risk = ticket.Risk,
                Reason = ticket.Reason
            },
            RequestId = entity.RequestId,
            Status = entity.Status,
            TransferType = entity.TransferType,
            RequiredApprovals = entity.RequiredApprovals,
            ApproverIds = DeserializeList(entity.ApproversJson),
            ApprovedBy = DeserializeList(entity.ApprovedByJson),
            CreatedUtc = entity.CreatedUtc,
            UpdatedUtc = entity.UpdatedUtc,
            CompletedUtc = entity.CompletedUtc
        };
    }

    private async Task RecordLedgerEventAsync(
        OwnershipTransferRequest entity,
        string eventType,
        CancellationToken cancellationToken,
        string? reason = null,
        string? actorId = null)
    {
        var payload = new
        {
            request_id = entity.RequestId,
            tenant_id = entity.TenantId,
            from_user_id = entity.FromUserId,
            to_user_id = entity.ToUserId,
            transfer_type = entity.TransferType,
            status = entity.Status,
            required_approvals = entity.RequiredApprovals,
            approved_by = DeserializeList(entity.ApprovedByJson),
            actor_id = actorId,
            reason
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(payloadJson);
        var canonicalPayload = CanonicalJson.Canonicalize(document.RootElement);
        var prevHash = await _ledgerRepository.GetLatestHashAsync(entity.RequestId, cancellationToken)
            .ConfigureAwait(false);
        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            entity.RequestId,
            eventType,
            canonicalPayload,
            prevHash ?? string.Empty);
        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        var list = JsonSerializer.Deserialize<List<string>>(json);
        return list ?? new List<string>();
    }

    private static bool IsSupportedTransferType(string transferType)
    {
        return transferType is "transfer" or "delegate";
    }

    private static bool IsRevocable(string status)
    {
        return status.Equals("pending", StringComparison.OrdinalIgnoreCase)
            || status.Equals("approved", StringComparison.OrdinalIgnoreCase);
    }
}
#nullable disable
