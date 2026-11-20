using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Provenance;

public sealed class LedgerTombstoneServiceTests
{
    [Fact]
    public async Task TombstoneAsync_AppendsEvent()
    {
        var repo = new FakeLedgerRepository();
        var policy = new LedgerRedactionPolicy(true, new[] { "token" });
        var factory = new LedgerEventFactory(new NoopSignatureProvider(), redactionPolicy: policy);
        var service = new LedgerTombstoneService(repo, factory, policy);

        await service.TombstoneAsync("trace-1", "cleanup", CancellationToken.None);

        Assert.NotNull(repo.LastEvent);
        Assert.Equal("ledger.tombstone", repo.LastEvent!.EventType);
    }

    private sealed class FakeLedgerRepository : ILedgerRepository
    {
        public LedgerEvent? LastEvent { get; private set; }

        public Task<string?> GetLatestHashAsync(string traceId, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<LedgerEvent>> GetByTraceIdAsync(string traceId, int skip, int take, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LedgerEvent>>(Array.Empty<LedgerEvent>());

        public Task<int> GetCountAsync(string traceId, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<IReadOnlyList<LedgerEvent>> SearchAsync(DateTimeOffset? startUtc, DateTimeOffset? endUtc, string? eventType, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LedgerEvent>>(Array.Empty<LedgerEvent>());

        public Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
        {
            LastEvent = ledgerEvent;
            return Task.CompletedTask;
        }
    }
}
