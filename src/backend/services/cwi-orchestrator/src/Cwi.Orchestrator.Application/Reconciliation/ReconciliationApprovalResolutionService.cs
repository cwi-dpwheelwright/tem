using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Reconciliation;

public sealed class ReconciliationApprovalResolutionService : DomainServiceBase
{
    private const string TicketPrefix = "reconcile:";

    private readonly IApprovalTicketRepository _ticketRepository;
    private readonly ICanonicalRecordRepository _recordRepository;
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;

    public ReconciliationApprovalResolutionService(
        IApprovalTicketRepository ticketRepository,
        ICanonicalRecordRepository recordRepository,
        ILedgerRepository ledgerRepository,
        LedgerEventFactory ledgerEventFactory)
    {
        _ticketRepository = ticketRepository;
        _recordRepository = recordRepository;
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
    }

    public Task<ServiceResponse<ReconciliationApprovalCompleteResponseDto>> CompleteAsync(
        string recordId,
        ApprovalCompleteRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleAsync(recordId, request, cancellationToken));
    }

    private async Task<ServiceResponse<ReconciliationApprovalCompleteResponseDto>> HandleAsync(
        string recordId,
        ApprovalCompleteRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recordId))
        {
            return ServiceResponse<ReconciliationApprovalCompleteResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "record_id is required."),
                statusCode: 400,
                message: "record_id required");
        }

        if (string.IsNullOrWhiteSpace(request.TicketId))
        {
            return ServiceResponse<ReconciliationApprovalCompleteResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "ticket_id is required."),
                statusCode: 400,
                message: "ticket_id required");
        }

        if (string.IsNullOrWhiteSpace(request.Decision))
        {
            return ServiceResponse<ReconciliationApprovalCompleteResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "decision is required."),
                statusCode: 400,
                message: "decision required");
        }

        var decision = request.Decision.Trim().ToLowerInvariant();
        if (decision is not ("approve" or "deny"))
        {
            return ServiceResponse<ReconciliationApprovalCompleteResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "decision must be approve or deny."),
                statusCode: 400,
                message: "invalid decision");
        }

        var ticket = await _ticketRepository
            .GetByTicketIdAsync(request.TicketId, cancellationToken)
            .ConfigureAwait(false);

        if (ticket is null)
        {
            return ServiceResponse<ReconciliationApprovalCompleteResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "ticket not found."),
                statusCode: 404,
                message: "ticket not found");
        }

        if (!ticket.IdempotencyKey.StartsWith(TicketPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResponse<ReconciliationApprovalCompleteResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "ticket does not belong to reconciliation."),
                statusCode: 400,
                message: "invalid ticket");
        }

        var ticketRecordId = ticket.IdempotencyKey.Substring(TicketPrefix.Length);
        if (!ticketRecordId.Equals(recordId, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResponse<ReconciliationApprovalCompleteResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "record_id does not match ticket."),
                statusCode: 409,
                message: "record_id mismatch");
        }

        var record = await _recordRepository.GetAsync(recordId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return ServiceResponse<ReconciliationApprovalCompleteResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "record not found."),
                statusCode: 404,
                message: "record not found");
        }

        var targetStatus = decision == "approve" ? "approved" : "denied";
        var normalizedStatus = ticket.Status?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedStatus is "approved" or "denied")
        {
            if (normalizedStatus.Equals(targetStatus, StringComparison.OrdinalIgnoreCase))
            {
                var existingConflicts = DeserializeConflicts(record.ConflictsJson);
                return ServiceResponse<ReconciliationApprovalCompleteResponseDto>.Success(new ReconciliationApprovalCompleteResponseDto
                {
                    RecordId = record.RecordId,
                    TicketId = ticket.TicketId,
                    Status = ticket.Status ?? targetStatus,
                    Decision = decision,
                    ConflictsCount = existingConflicts.Count,
                    UpdatedUtc = record.UpdatedUtc
                });
            }

            return ServiceResponse<ReconciliationApprovalCompleteResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "ticket already resolved."),
                statusCode: 409,
                message: "ticket already resolved");
        }

        ticket.Status = targetStatus;
        ticket.UpdatedUtc = DateTimeOffset.UtcNow;
        await _ticketRepository.UpdateAsync(ticket, cancellationToken).ConfigureAwait(false);

        var canonical = DeserializeCanonical(record.CanonicalJson);
        var conflicts = DeserializeConflicts(record.ConflictsJson);
        if (decision == "approve")
        {
            conflicts = new List<ConflictEntryDto>();
        }

        canonical["_reconciliation"] = new Dictionary<string, object?>
        {
            ["status"] = targetStatus,
            ["decision"] = decision,
            ["ticket_id"] = ticket.TicketId,
            ["approver_id"] = request.ApproverId,
            ["reason"] = request.Reason,
            ["decided_utc"] = ticket.UpdatedUtc
        };

        record.CanonicalJson = JsonSerializer.Serialize(canonical);
        record.ConflictsJson = JsonSerializer.Serialize(conflicts);
        record.UpdatedUtc = ticket.UpdatedUtc;
        await _recordRepository.UpsertAsync(record, cancellationToken).ConfigureAwait(false);

        var payload = new
        {
            record_id = record.RecordId,
            ticket_id = ticket.TicketId,
            decision,
            approver_id = request.ApproverId,
            reason = request.Reason,
            conflicts_count = conflicts.Count,
            updated_utc = record.UpdatedUtc
        };
        var payloadJson = JsonSerializer.Serialize(payload);
        var prevHash = await _ledgerRepository.GetLatestHashAsync(record.RecordId, cancellationToken).ConfigureAwait(false);
        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId: record.RecordId,
            eventType: "reconciliation.approval_decision",
            payloadJson: payloadJson,
            prevHash: prevHash ?? string.Empty);
        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<ReconciliationApprovalCompleteResponseDto>.Success(new ReconciliationApprovalCompleteResponseDto
        {
            RecordId = record.RecordId,
            TicketId = ticket.TicketId,
            Status = ticket.Status ?? targetStatus,
            Decision = decision,
            ConflictsCount = conflicts.Count,
            UpdatedUtc = record.UpdatedUtc
        });
    }

    private static Dictionary<string, object?> DeserializeCanonical(string canonicalJson)
    {
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(canonicalJson) ?? new Dictionary<string, object?>();
    }

    private static List<ConflictEntryDto> DeserializeConflicts(string conflictsJson)
    {
        return JsonSerializer.Deserialize<List<ConflictEntryDto>>(conflictsJson) ?? new List<ConflictEntryDto>();
    }
}
