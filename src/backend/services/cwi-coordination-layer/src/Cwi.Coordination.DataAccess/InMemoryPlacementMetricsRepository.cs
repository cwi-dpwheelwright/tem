using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.DataAccess;

public sealed class InMemoryPlacementMetricsRepository : IPlacementMetricsRepository
{
    private readonly List<PlacementMetric> _metrics = new();

    public Task AddAsync(PlacementMetric metric, CancellationToken cancellationToken)
    {
        lock (_metrics)
        {
            _metrics.Add(metric);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PlacementMetric>> ListAsync(
        string? nodeId,
        int limit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PlacementMetric> snapshot;
        lock (_metrics)
        {
            var query = _metrics.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(nodeId))
            {
                query = query.Where(metric => string.Equals(metric.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
            }

            query = query.OrderByDescending(metric => metric.ObservedUtc);
            if (limit > 0)
            {
                query = query.Take(limit);
            }

            snapshot = query.ToList();
        }

        return Task.FromResult(snapshot);
    }
}
