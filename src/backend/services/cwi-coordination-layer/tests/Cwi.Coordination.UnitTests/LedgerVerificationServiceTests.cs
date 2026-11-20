using Cwi.Coordination.Application.Services;
using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;
using FluentAssertions;

namespace Cwi.Coordination.UnitTests;

public sealed class LedgerVerificationServiceTests
{
    [Fact]
    public async Task VerifyAsync_Returns_Valid_For_Intact_Ledger()
    {
        var repo = new FakeLedgerRepository();
        var ledger = new LedgerService(repo);
        await ledger.AppendAsync("membership_enroll", new { node_id = "node-1" }, CancellationToken.None);
        await ledger.AppendAsync("round_create", new { round_id = Guid.NewGuid() }, CancellationToken.None);

        var verifier = new LedgerVerificationService(repo);
        var result = await verifier.VerifyAsync(100, CancellationToken.None);

        result.Valid.Should().BeTrue();
        result.CheckedEvents.Should().Be(2);
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_Returns_Failure_When_Hash_Mismatch()
    {
        var repo = new FakeLedgerRepository();
        var ledger = new LedgerService(repo);
        await ledger.AppendAsync("membership_enroll", new { node_id = "node-1" }, CancellationToken.None);
        await ledger.AppendAsync("round_create", new { round_id = Guid.NewGuid() }, CancellationToken.None);

        repo.CorruptLastHash();

        var verifier = new LedgerVerificationService(repo);
        var result = await verifier.VerifyAsync(100, CancellationToken.None);

        result.Valid.Should().BeFalse();
        result.FailureReason.Should().Be("hash mismatch");
        result.FailureEventId.Should().NotBeNull();
    }

    private sealed class FakeLedgerRepository : ILedgerRepository
    {
        private readonly List<LedgerEvent> _events = new();

        public Task<LedgerEvent> AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
        {
            _events.Add(ledgerEvent);
            return Task.FromResult(ledgerEvent);
        }

        public Task<IReadOnlyList<LedgerEvent>> ListAsync(int limit, CancellationToken cancellationToken)
        {
            var take = Math.Max(limit, 0);
            var result = _events
                .OrderByDescending(evt => evt.CreatedUtc)
                .Take(take)
                .ToList();
            return Task.FromResult((IReadOnlyList<LedgerEvent>)result);
        }

        public void CorruptLastHash()
        {
            if (_events.Count == 0)
            {
                return;
            }

            var last = _events[^1];
            _events[^1] = new LedgerEvent
            {
                Id = last.Id,
                EventType = last.EventType,
                PayloadJson = last.PayloadJson,
                PrevHash = last.PrevHash,
                Hash = "corrupted",
                CreatedUtc = last.CreatedUtc
            };
        }
    }
}
