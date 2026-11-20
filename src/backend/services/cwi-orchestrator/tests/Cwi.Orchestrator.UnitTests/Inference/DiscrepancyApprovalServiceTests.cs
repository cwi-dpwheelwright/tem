#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cwi.Orchestrator.Application.Inference;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Inference;

public class DiscrepancyApprovalServiceTests
{
    [Fact]
    public async Task CreateIfRequiredAsync_creates_ticket_and_ledger_event()
    {
        var tickets = new InMemoryApprovalTicketRepository();
        var ledger = new InMemoryLedgerRepository();
        var factory = new LedgerEventFactory(new NoopSignatureProvider(), redactionPolicy: new LedgerRedactionPolicy(false, Array.Empty<string>()));
        var ledgerRecorder = new InferenceLedgerRecorder(ledger, factory);
        var service = new DiscrepancyApprovalService(tickets, ledgerRecorder);

        var evaluation = new DiscrepancyEvaluation
        {
            Severity = "high",
            Score = 0.95,
            Reason = "conflicting sources"
        };

        var result = await service.CreateIfRequiredAsync("tenant", "user", "trace", evaluation, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Created.Should().BeTrue();
        tickets.Items.Should().HaveCount(1);
        ledger.Events.Should().ContainSingle(e => e.EventType == "DISCREPANCY_APPROVAL_REQUESTED");
    }

    private sealed class InMemoryApprovalTicketRepository : IApprovalTicketRepository
    {
        private readonly List<ApprovalTicket> _tickets = new();

        public IReadOnlyList<ApprovalTicket> Items => _tickets;

        public Task<ApprovalTicket?> GetByTicketIdAsync(string ticketId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_tickets.FirstOrDefault(t => t.TicketId == ticketId));
        }

        public Task<ApprovalTicket?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
        {
            return Task.FromResult(_tickets.FirstOrDefault(t => t.IdempotencyKey == idempotencyKey));
        }

        public Task<IReadOnlyList<ApprovalTicket>> ListByTenantIdAsync(string tenantId, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ApprovalTicket>>(_tickets);
        }

        public Task AddAsync(ApprovalTicket ticket, CancellationToken cancellationToken)
        {
            _tickets.Add(ticket);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ApprovalTicket ticket, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryLedgerRepository : ILedgerRepository
    {
        private readonly List<LedgerEvent> _events = new();

        public IReadOnlyList<LedgerEvent> Events => _events;

        public Task<string?> GetLatestHashAsync(string traceId, CancellationToken cancellationToken)
        {
            var latest = _events.LastOrDefault(e => e.TraceId == traceId);
            return Task.FromResult(latest?.Hash);
        }

        public Task<IReadOnlyList<LedgerEvent>> GetByTraceIdAsync(string traceId, int skip, int take, CancellationToken cancellationToken)
        {
            var items = _events.Where(e => e.TraceId == traceId).Skip(skip).Take(take).ToList();
            return Task.FromResult<IReadOnlyList<LedgerEvent>>(items);
        }

        public Task<int> GetCountAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_events.Count(e => e.TraceId == traceId));
        }

        public Task<IReadOnlyList<LedgerEvent>> SearchAsync(
            DateTimeOffset? startUtc,
            DateTimeOffset? endUtc,
            string? eventType,
            CancellationToken cancellationToken)
        {
            var items = _events.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(eventType))
            {
                items = items.Where(e => string.Equals(e.EventType, eventType, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyList<LedgerEvent>>(items.ToList());
        }

        public Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
        {
            _events.Add(ledgerEvent);
            return Task.CompletedTask;
        }
    }
}
#nullable disable
