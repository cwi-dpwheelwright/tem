using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Coordination.DataAccess.Repositories;

public sealed class PlacementMetricsRepository : IPlacementMetricsRepository
{
    private readonly CoordinationDbContext _db;

    public PlacementMetricsRepository(CoordinationDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(PlacementMetric metric, CancellationToken cancellationToken)
    {
        _db.PlacementMetrics.Add(metric);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlacementMetric>> ListAsync(
        string? nodeId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = _db.PlacementMetrics.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            query = query.Where(metric => metric.NodeId == nodeId);
        }

        query = query.OrderByDescending(metric => metric.ObservedUtc);
        if (limit > 0)
        {
            query = query.Take(limit);
        }

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }
}
