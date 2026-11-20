using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Coordination.DataAccess.Repositories;

public sealed class DegradationEventRepository : IDegradationEventRepository
{
    private readonly CoordinationDbContext _db;

    public DegradationEventRepository(CoordinationDbContext db)
    {
        _db = db;
    }

    public async Task AppendAsync(DegradationEvent degradationEvent, CancellationToken cancellationToken)
    {
        _db.DegradationEvents.Add(degradationEvent);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DegradationEvent>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        var take = Math.Max(limit, 0);
        return await _db.DegradationEvents
            .OrderByDescending(x => x.ReceivedUtc)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
