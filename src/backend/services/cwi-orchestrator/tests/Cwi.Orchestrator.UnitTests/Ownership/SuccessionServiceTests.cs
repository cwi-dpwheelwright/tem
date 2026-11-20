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

public class SuccessionServiceTests
{
    [Fact]
    public async Task Trigger_auto_approves_when_not_required()
    {
        var repo = new InMemoryOwnershipTransferRepository();
        var tickets = new InMemoryApprovalTicketRepository();
        var ledger = new InMemoryLedgerRepository();
        var plans = new InMemorySuccessionPlanRepository();
        var tenantConfig = new InMemoryTenantConfigRepository();
        var transferService = new OwnershipTransferService(
            repo,
            tickets,
            ledger,
            new LedgerEventFactory(new NoopSignatureProvider()),
            new OwnershipPolicyEvaluator(),
            new OrchestratorMetrics());
        var service = new SuccessionService(
            plans,
            tenantConfig,
            transferService,
            ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));

        var plan = await service.CreatePlanAsync(new SuccessionPlanRequestDto
        {
            TenantId = "tenant",
            AssetId = "asset-1",
            CurrentOwnerId = "owner-1",
            Successors = new List<SuccessionSuccessorDto>
            {
                new() { OwnerId = "owner-2", Order = 1, Trigger = "retirement", ApprovalRequired = false }
            }
        }, CancellationToken.None);

        var triggered = await service.TriggerAsync(new SuccessionTriggerRequestDto
        {
            TenantId = "tenant",
            PlanId = plan.Data!.PlanId,
            ActorId = "owner-1",
            Reason = "retirement"
        }, CancellationToken.None);

        triggered.IsSuccess.Should().BeTrue();
        triggered.Data!.Status.Should().Be("executed");
        triggered.Data!.ContinuitySnapshotId.Should().NotBeNullOrWhiteSpace();

        var transfer = await transferService.GetAsync(triggered.Data.TransferRequestId!, CancellationToken.None);
        transfer.Data!.Status.Should().Be("approved");
        ledger.Events.Should().Contain(e => e.EventType == "ownership.succession_triggered");
    }

    [Fact]
    public async Task Restore_rehydrates_snapshot()
    {
        var repo = new InMemoryOwnershipTransferRepository();
        var tickets = new InMemoryApprovalTicketRepository();
        var ledger = new InMemoryLedgerRepository();
        var plans = new InMemorySuccessionPlanRepository();
        var tenantConfig = new InMemoryTenantConfigRepository();
        var transferService = new OwnershipTransferService(
            repo,
            tickets,
            ledger,
            new LedgerEventFactory(new NoopSignatureProvider()),
            new OwnershipPolicyEvaluator(),
            new OrchestratorMetrics());
        var service = new SuccessionService(
            plans,
            tenantConfig,
            transferService,
            ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));

        var plan = await service.CreatePlanAsync(new SuccessionPlanRequestDto
        {
            TenantId = "tenant",
            AssetId = "asset-1",
            CurrentOwnerId = "owner-1",
            Successors = new List<SuccessionSuccessorDto>
            {
                new() { OwnerId = "owner-2", Order = 1, Trigger = "retirement", ApprovalRequired = false }
            }
        }, CancellationToken.None);

        var triggered = await service.TriggerAsync(new SuccessionTriggerRequestDto
        {
            TenantId = "tenant",
            PlanId = plan.Data!.PlanId,
            ActorId = "owner-1"
        }, CancellationToken.None);

        var restore = await service.RestoreAsync(new SuccessionRestoreRequestDto
        {
            TenantId = "tenant",
            SnapshotId = triggered.Data!.ContinuitySnapshotId!,
            ActorId = "owner-1"
        }, CancellationToken.None);

        restore.IsSuccess.Should().BeTrue();
        restore.Data!.Status.Should().Be("restored");
        ledger.Events.Should().Contain(e => e.EventType == "ownership.succession_restored");
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
            var results = _requests.Where(x => x.TenantId == tenantId).ToList();
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

        public Task<IReadOnlyList<LedgerEvent>> SearchAsync(System.DateTimeOffset? startUtc, System.DateTimeOffset? endUtc, string? eventType, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LedgerEvent>>(Events);
        }

        public Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
        {
            Events.Add(ledgerEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySuccessionPlanRepository : ISuccessionPlanRepository
    {
        private readonly List<SuccessionPlan> _plans = new();
        private readonly List<SuccessionSnapshot> _snapshots = new();

        public Task<SuccessionPlan?> GetAsync(string tenantId, string planId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_plans.FirstOrDefault(x =>
                x.TenantId == tenantId && x.PlanId == planId));
        }

        public Task AddAsync(SuccessionPlan plan, CancellationToken cancellationToken)
        {
            _plans.Add(plan);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(SuccessionPlan plan, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task AddSnapshotAsync(SuccessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            _snapshots.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task<SuccessionSnapshot?> GetSnapshotAsync(
            string tenantId,
            string snapshotId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_snapshots.FirstOrDefault(x =>
                x.TenantId == tenantId && x.SnapshotId == snapshotId));
        }
    }

    private sealed class InMemoryTenantConfigRepository : ITenantConfigRepository
    {
        public Task<TenantConfig?> GetAsync(string tenantId, CancellationToken cancellationToken)
        {
            return Task.FromResult<TenantConfig?>(null);
        }

        public Task UpsertAsync(TenantConfig config, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
#nullable disable
