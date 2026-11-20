using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.DataAccess;

public sealed class InMemoryLedgerRepository : ILedgerRepository
{
    private readonly List<LedgerEvent> _events = new();

    public Task<LedgerEvent> AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
    {
        _events.Add(ledgerEvent);
        return Task.FromResult(ledgerEvent);
    }

    public Task<IReadOnlyList<LedgerEvent>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        var result = _events
            .OrderByDescending(e => e.CreatedUtc)
            .Take(limit)
            .ToList();
        return Task.FromResult((IReadOnlyList<LedgerEvent>)result);
    }
}
