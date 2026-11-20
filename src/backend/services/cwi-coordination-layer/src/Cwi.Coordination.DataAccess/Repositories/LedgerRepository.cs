using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Coordination.DataAccess.Repositories;

public sealed class LedgerRepository : ILedgerRepository
{
    private readonly CoordinationDbContext _db;

    public LedgerRepository(CoordinationDbContext db)
    {
        _db = db;
    }

    public async Task<LedgerEvent> AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
    {
        _db.LedgerEvents.Add(ledgerEvent);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ledgerEvent;
    }

    public async Task<IReadOnlyList<LedgerEvent>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        var take = Math.Max(limit, 0);
        return await _db.LedgerEvents
            .OrderByDescending(e => e.CreatedUtc)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
