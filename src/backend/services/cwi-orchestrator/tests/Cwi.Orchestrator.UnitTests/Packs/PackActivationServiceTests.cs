#nullable enable
using Cwi.Orchestrator.Application.Packs.Activation;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Packs;

public sealed class PackActivationServiceTests
{
    [Fact]
    public async Task ActivateAsync_IsIdempotent_ForSamePackHash()
    {
        var ledger = new InMemoryLedgerRepository();
        var audit = new PackActivationAuditRecorder(ledger, new LedgerEventFactory(new NoopSignatureProvider()));
        var repo = new InMemoryPackActivationRepository();
        var service = new PackActivationService(repo, audit);

        var request = new PackActivationRequestDto
        {
            TenantId = "tenant-1",
            PackName = "pack-a",
            PackHash = "hash-1",
            ActorUserId = "user-1"
        };

        var first = await service.ActivateAsync(request, CancellationToken.None);
        var second = await service.ActivateAsync(request, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        ledger.Events.Should().HaveCount(1);
    }

    [Fact]
    public async Task RollbackAsync_IsIdempotent_WhenAlreadyRolledBack()
    {
        var ledger = new InMemoryLedgerRepository();
        var audit = new PackActivationAuditRecorder(ledger, new LedgerEventFactory(new NoopSignatureProvider()));
        var repo = new InMemoryPackActivationRepository();
        var service = new PackActivationService(repo, audit);

        var activation = new PackActivationRequestDto
        {
            TenantId = "tenant-1",
            PackName = "pack-a",
            PackHash = "hash-1",
            ActorUserId = "user-1"
        };

        await service.ActivateAsync(activation, CancellationToken.None);

        var rollback = new PackRollbackRequestDto
        {
            TenantId = "tenant-1",
            PackName = "pack-a",
            ActorUserId = "user-2",
            Reason = "rollback"
        };

        var first = await service.RollbackAsync(rollback, CancellationToken.None);
        var second = await service.RollbackAsync(rollback, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        ledger.Events.Should().HaveCount(2);
    }

    private sealed class InMemoryPackActivationRepository : IPackActivationRepository
    {
        private readonly Dictionary<(string TenantId, string PackName), PackActivationState> _states = new();

        public Task<PackActivationState?> GetAsync(string tenantId, string packName, CancellationToken cancellationToken)
        {
            _states.TryGetValue((tenantId, packName), out var state);
            return Task.FromResult(state);
        }

        public Task UpsertAsync(PackActivationState state, CancellationToken cancellationToken)
        {
            _states[(state.TenantId, state.PackName)] = state;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryLedgerRepository : ILedgerRepository
    {
        private readonly List<LedgerEvent> _events = new();

        public IReadOnlyList<LedgerEvent> Events => _events;

        public Task<string?> GetLatestHashAsync(string traceId, CancellationToken cancellationToken)
        {
            var latest = _events.LastOrDefault(x => x.TraceId == traceId);
            return Task.FromResult(latest?.Hash);
        }

        public Task<IReadOnlyList<LedgerEvent>> GetByTraceIdAsync(string traceId, int skip, int take, CancellationToken cancellationToken)
        {
            var items = _events.Where(x => x.TraceId == traceId).Skip(skip).Take(take).ToList();
            return Task.FromResult<IReadOnlyList<LedgerEvent>>(items);
        }

        public Task<int> GetCountAsync(string traceId, CancellationToken cancellationToken)
        {
            var count = _events.Count(x => x.TraceId == traceId);
            return Task.FromResult(count);
        }

        public Task<IReadOnlyList<LedgerEvent>> SearchAsync(
            DateTimeOffset? startUtc,
            DateTimeOffset? endUtc,
            string? eventType,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LedgerEvent>>(_events.ToList());
        }

        public Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
        {
            _events.Add(ledgerEvent);
            return Task.CompletedTask;
        }
    }
}
#nullable disable
