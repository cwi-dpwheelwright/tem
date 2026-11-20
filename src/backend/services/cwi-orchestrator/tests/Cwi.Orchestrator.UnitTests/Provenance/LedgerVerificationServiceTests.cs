using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Provenance;

public class LedgerVerificationServiceTests
{
    [Fact]
    public async Task VerifyAsync_flags_tampered_hash()
    {
        var traceId = "trace-1";
        var events = new List<LedgerEvent>();
        var first = LedgerEvent.Create(traceId, "evt", "payload", prevHash: string.Empty);
        events.Add(first);
        var second = LedgerEvent.Create(traceId, "evt2", "payload2", prevHash: first.Hash);
        second.Hash = "tampered";
        events.Add(second);

        var repo = new FakeLedgerRepository(events);
        var service = new LedgerVerificationService(repo, new NoopSignatureProvider());

        var result = await service.VerifyAsync(traceId, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("Hash mismatch");
    }

    private sealed class FakeLedgerRepository : ILedgerRepository
    {
        private readonly IReadOnlyList<LedgerEvent> _events;

        public FakeLedgerRepository(IReadOnlyList<LedgerEvent> events)
        {
            _events = events;
        }

        public Task<string?> GetLatestHashAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<IReadOnlyList<LedgerEvent>> GetByTraceIdAsync(string traceId, int skip, int take, CancellationToken cancellationToken)
        {
            return Task.FromResult(_events);
        }

        public Task<int> GetCountAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_events.Count);
        }

        public Task<IReadOnlyList<LedgerEvent>> SearchAsync(
            DateTimeOffset? startUtc,
            DateTimeOffset? endUtc,
            string? eventType,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_events);
        }

        public Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
