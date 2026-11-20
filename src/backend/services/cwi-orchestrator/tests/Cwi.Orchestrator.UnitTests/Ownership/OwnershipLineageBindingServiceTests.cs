#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Ownership;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Ownership;

public class OwnershipLineageBindingServiceTests
{
    [Fact]
    public async Task Bind_records_ledger_and_returns_lineage()
    {
        var repo = new InMemoryOwnershipLineageRepository();
        var ledger = new InMemoryLedgerRepository();
        var service = new OwnershipLineageBindingService(
            repo,
            ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));

        var bind = await service.BindAsync(new OwnershipLineageBindingRequestDto
        {
            TenantId = "tenant",
            AssetId = "asset-1",
            OwnerId = "owner-1",
            Relationship = "owner",
            ParentAssetId = "asset-root"
        }, CancellationToken.None);

        bind.IsSuccess.Should().BeTrue();
        ledger.Events.Should().Contain(e => e.EventType == "ownership.lineage_bound");

        var lineage = await service.GetAssetLineageAsync("tenant", CancellationToken.None);
        lineage.Data!.Nodes.Should().NotBeEmpty();
        lineage.Data!.Edges.Should().NotBeEmpty();
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

    private sealed class InMemoryOwnershipLineageRepository : IOwnershipLineageRepository
    {
        private readonly List<OwnershipLineageBinding> _records = new();

        public Task AddAsync(OwnershipLineageBinding binding, CancellationToken cancellationToken)
        {
            _records.Add(binding);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OwnershipLineageBinding>> ListByTenantAsync(
            string tenantId,
            CancellationToken cancellationToken)
        {
            var results = _records
                .Where(x => x.TenantId == tenantId)
                .OrderByDescending(x => x.CreatedUtc)
                .ToList();
            return Task.FromResult<IReadOnlyList<OwnershipLineageBinding>>(results);
        }
    }
}
#nullable disable
