using Cwi.Coordination.Domain.Entities;

namespace Cwi.Coordination.Domain.Repositories;

public interface IPlacementMetricsRepository
{
    Task AddAsync(PlacementMetric metric, CancellationToken cancellationToken);
    Task<IReadOnlyList<PlacementMetric>> ListAsync(string? nodeId, int limit, CancellationToken cancellationToken);
}
