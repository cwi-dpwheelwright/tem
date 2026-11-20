#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Ownership;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Application.Telemetry;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Ownership;

public class OwnershipTransferServiceTests
{
    [Fact]
    public async Task CreateAsync_creates_request_and_records_ledger_event()
    {
        var repo = new InMemoryOwnershipTransferRepository();
        var ledger = new InMemoryLedgerRepository();
        var tickets = new InMemoryApprovalTicketRepository();
        var service = new OwnershipTransferService(
            repo,
            tickets,
            ledger,
            new LedgerEventFactory(new NoopSignatureProvider()),
            new OwnershipPolicyEvaluator(),
            new OrchestratorMetrics());

        var request = new OwnershipTransferRequestDto
        {
            TenantId = "tenant",
            FromUserId = "owner",
            ToUserId = "delegate",
            TransferType = "delegate",
            Reason = "delegate access",
            RequiredApprovals = 1,
            ApproverIds = new List<string> { "approver" }
        };

        var response = await service.CreateAsync(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Status.Should().Be("pending");
        response.Data.ApprovalTicket.Should().NotBeNull();
        ledger.Events.Should().HaveCount(1);
    }

    [Fact]
    public async Task DecideAsync_approves_when_required_votes_met()
    {
        var repo = new InMemoryOwnershipTransferRepository();
        var ledger = new InMemoryLedgerRepository();
        var tickets = new InMemoryApprovalTicketRepository();
        var service = new OwnershipTransferService(
            repo,
            tickets,
            ledger,
            new LedgerEventFactory(new NoopSignatureProvider()),
            new OwnershipPolicyEvaluator(),
            new OrchestratorMetrics());

        var created = await service.CreateAsync(new OwnershipTransferRequestDto
        {
            TenantId = "tenant",
            FromUserId = "owner",
            ToUserId = "new-owner",
            RequiredApprovals = 1,
            ApproverIds = new List<string> { "approver" }
        }, CancellationToken.None);

        var decision = new OwnershipTransferDecisionRequestDto
        {
            ApproverId = "approver",
            Decision = "approve"
        };

        await service.ApplyApprovalDecisionAsync(created.Data!.ApprovalTicket!.TicketId, decision.Decision, decision.ApproverId, null, CancellationToken.None);
        var response = await service.GetAsync(created.Data!.RequestId, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Status.Should().Be("approved");
        ledger.Events.Should().HaveCount(3);
    }

    [Fact]
    public async Task RevokeAsync_marks_request_revoked_and_records_ledger()
    {
        var repo = new InMemoryOwnershipTransferRepository();
        var ledger = new InMemoryLedgerRepository();
        var tickets = new InMemoryApprovalTicketRepository();
        var service = new OwnershipTransferService(
            repo,
            tickets,
            ledger,
            new LedgerEventFactory(new NoopSignatureProvider()),
            new OwnershipPolicyEvaluator(),
            new OrchestratorMetrics());

        var created = await service.CreateAsync(new OwnershipTransferRequestDto
        {
            TenantId = "tenant",
            FromUserId = "owner",
            ToUserId = "delegate",
            TransferType = "delegate",
            Reason = "delegate access",
            RequiredApprovals = 1,
            ApproverIds = new List<string> { "owner" }
        }, CancellationToken.None);

        var revoked = await service.RevokeAsync(new OwnershipTransferRevokeRequestDto
        {
            RequestId = created.Data!.RequestId,
            ActorId = "owner",
            Reason = "cancel"
        }, CancellationToken.None);

        revoked.IsSuccess.Should().BeTrue();
        revoked.Data!.Status.Should().Be("revoked");
        ledger.Events.Should().Contain(e => e.EventType == "OWNERSHIP_TRANSFER_REVOKED");
    }

    private sealed class InMemoryOwnershipTransferRepository : IOwnershipTransferRepository
    {
        private readonly List<OwnershipTransferRequest> _requests = new();

        public Task<OwnershipTransferRequest?> GetByRequestIdAsync(string requestId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_requests.Find(x => x.RequestId == requestId));
        }

        public Task<OwnershipTransferRequest?> GetByApprovalTicketIdAsync(string approvalTicketId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_requests.Find(x => x.ApprovalTicketId == approvalTicketId));
        }

        public Task<OwnershipTransferRequest?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
        {
            return Task.FromResult(_requests.Find(x => x.IdempotencyKey == idempotencyKey));
        }

        public Task<IReadOnlyList<OwnershipTransferRequest>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken)
        {
            var results = _requests
                .Where(x => x.TenantId == tenantId)
                .ToList();
            return Task.FromResult<IReadOnlyList<OwnershipTransferRequest>>(results);
        }

        public Task AddAsync(OwnershipTransferRequest request, CancellationToken cancellationToken)
        {
            _requests.Add(request);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(OwnershipTransferRequest request, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryLedgerRepository : ILedgerRepository
    {
        public List<LedgerEvent> Events { get; } = new();

        public Task<string?> GetLatestHashAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(Events.LastOrDefault()?.Hash);
        }

        public Task<IReadOnlyList<LedgerEvent>> GetByTraceIdAsync(string traceId, int skip, int take, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LedgerEvent>>(Events);
        }

        public Task<int> GetCountAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Events.Count);
        }

        public Task<IReadOnlyList<LedgerEvent>> SearchAsync(DateTimeOffset? startUtc, DateTimeOffset? endUtc, string? eventType, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LedgerEvent>>(Events);
        }

        public Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
        {
            Events.Add(ledgerEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryApprovalTicketRepository : IApprovalTicketRepository
    {
        private readonly List<ApprovalTicket> _tickets = new();

        public Task<ApprovalTicket?> GetByTicketIdAsync(string ticketId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_tickets.Find(x => x.TicketId == ticketId));
        }

        public Task<ApprovalTicket?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
        {
            return Task.FromResult(_tickets.Find(x => x.IdempotencyKey == idempotencyKey));
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
}
#nullable disable
