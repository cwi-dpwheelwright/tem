using System.Text.Json;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Reconciliation;

public sealed class ReconciliationApprovalService
{
    private readonly IApprovalTicketRepository _ticketRepository;
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;

    public ReconciliationApprovalService(
        IApprovalTicketRepository ticketRepository,
        ILedgerRepository ledgerRepository,
        LedgerEventFactory ledgerEventFactory)
    {
        _ticketRepository = ticketRepository;
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
    }

    public async Task<ApprovalTicketSummaryDto?> EnsureApprovalAsync(
        string recordId,
        string tenantId,
        string userId,
        IReadOnlyList<ConflictEntryDto> conflicts,
        CancellationToken cancellationToken)
    {
        if (conflicts.Count == 0)
        {
            return null;
        }

        var resolvedTenant = string.IsNullOrWhiteSpace(tenantId) ? "unknown" : tenantId.Trim();
        var resolvedUser = string.IsNullOrWhiteSpace(userId) ? "system" : userId.Trim();
        var idempotencyKey = $"reconcile:{recordId}";

        var existing = await _ticketRepository
            .GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return new ApprovalTicketSummaryDto
            {
                TicketId = existing.TicketId,
                Risk = existing.Risk,
                Reason = existing.Reason
            };
        }

        var risk = "high";
        var reason = $"Reconciliation conflicts in {string.Join(", ", conflicts.Select(c => c.Field))}";
        var ticketId = Guid.NewGuid().ToString("N");
        var ticket = new ApprovalTicket
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            TenantId = resolvedTenant,
            UserId = resolvedUser,
            Risk = risk,
            Reason = reason,
            Status = "pending",
            IdempotencyKey = idempotencyKey,
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        await _ticketRepository.AddAsync(ticket, cancellationToken).ConfigureAwait(false);

        var payload = new
        {
            record_id = recordId,
            ticket_id = ticketId,
            tenant_id = resolvedTenant,
            risk = risk,
            conflicts = conflicts
        };
        var payloadJson = JsonSerializer.Serialize(payload);
        var prevHash = await _ledgerRepository.GetLatestHashAsync(recordId, cancellationToken).ConfigureAwait(false);
        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId: recordId,
            eventType: "reconciliation.approval_ticket",
            payloadJson: payloadJson,
            prevHash: prevHash ?? string.Empty);
        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);

        return new ApprovalTicketSummaryDto
        {
            TicketId = ticketId,
            Risk = risk,
            Reason = reason
        };
    }

    public async Task<ApprovalTicketSummaryDto?> GetExistingApprovalAsync(
        string recordId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = $"reconcile:{recordId}";
        var existing = await _ticketRepository
            .GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return null;
        }

        return new ApprovalTicketSummaryDto
        {
            TicketId = existing.TicketId,
            Risk = existing.Risk,
            Reason = existing.Reason
        };
    }
}
