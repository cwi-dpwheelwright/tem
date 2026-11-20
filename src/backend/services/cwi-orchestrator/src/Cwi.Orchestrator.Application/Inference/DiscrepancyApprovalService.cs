#nullable enable
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Inference;

public sealed class DiscrepancyApprovalService
{
    private const double ApprovalScoreThreshold = 0.75;
    private readonly IApprovalTicketRepository _approvalTicketRepository;
    private readonly InferenceLedgerRecorder _ledgerRecorder;

    public DiscrepancyApprovalService(
        IApprovalTicketRepository approvalTicketRepository,
        InferenceLedgerRecorder ledgerRecorder)
    {
        _approvalTicketRepository = approvalTicketRepository;
        _ledgerRecorder = ledgerRecorder;
    }

    public async Task<DiscrepancyApprovalResult?> CreateIfRequiredAsync(
        string tenantId,
        string userId,
        string traceId,
        DiscrepancyEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        if (!RequiresApproval(evaluation))
        {
            return null;
        }

        var idempotencyKey = $"{tenantId}:{userId}:{traceId}:discrepancy";
        var existing = await _approvalTicketRepository
            .GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            await RecordLedgerAsync(traceId, existing, evaluation, cancellationToken).ConfigureAwait(false);
            return new DiscrepancyApprovalResult(existing.TicketId, existing.Risk, existing.Reason, Created: false);
        }

        var ticketId = Guid.NewGuid().ToString("N");
        var reason = BuildReason(evaluation);
        var ticket = new ApprovalTicket
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            TenantId = tenantId,
            UserId = userId,
            Risk = evaluation.Severity,
            Reason = reason,
            Status = "pending",
            IdempotencyKey = idempotencyKey,
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        await _approvalTicketRepository.AddAsync(ticket, cancellationToken).ConfigureAwait(false);
        await RecordLedgerAsync(traceId, ticket, evaluation, cancellationToken).ConfigureAwait(false);

        return new DiscrepancyApprovalResult(ticketId, ticket.Risk, ticket.Reason, Created: true);
    }

    private static bool RequiresApproval(DiscrepancyEvaluation evaluation)
    {
        if (string.Equals(evaluation.Severity, "high", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return evaluation.Score >= ApprovalScoreThreshold;
    }

    private static string BuildReason(DiscrepancyEvaluation evaluation)
    {
        return string.IsNullOrWhiteSpace(evaluation.Reason)
            ? "High discrepancy requires approval."
            : evaluation.Reason!;
    }

    private Task RecordLedgerAsync(
        string traceId,
        ApprovalTicket ticket,
        DiscrepancyEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        return _ledgerRecorder.RecordDiscrepancyApprovalRequestedAsync(
            traceId,
            ticket.TicketId,
            evaluation.Severity,
            evaluation.Score,
            ticket.Reason,
            cancellationToken);
    }
}

public sealed record DiscrepancyApprovalResult(
    string TicketId,
    string Risk,
    string Reason,
    bool Created);
#nullable disable
