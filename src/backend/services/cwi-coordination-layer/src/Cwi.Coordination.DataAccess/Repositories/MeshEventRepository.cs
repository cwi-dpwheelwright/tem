using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Coordination.DataAccess.Repositories;

public sealed class MeshEventRepository : IMeshEventRepository
{
    private readonly CoordinationDbContext _db;

    public MeshEventRepository(CoordinationDbContext db)
    {
        _db = db;
    }

    public async Task AppendAsync(MeshEvent meshEvent, CancellationToken cancellationToken)
    {
        _db.MeshEvents.Add(meshEvent);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MeshEvent>> ListAsync(
        int limit,
        DateTimeOffset? sinceUtc,
        CancellationToken cancellationToken)
    {
        var take = Math.Max(limit, 0);
        var query = _db.MeshEvents.AsQueryable();
        if (sinceUtc.HasValue)
        {
            query = query.Where(x => x.CreatedUtc >= sinceUtc.Value);
        }

        return await query
            .OrderByDescending(x => x.CreatedUtc)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task PruneAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        var stale = await _db.MeshEvents
            .Where(x => x.CreatedUtc < cutoffUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (stale.Count == 0)
        {
            return;
        }

        _db.MeshEvents.RemoveRange(stale);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
