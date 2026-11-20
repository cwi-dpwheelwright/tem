using Cwi.Coordination.Domain.Entities;

namespace Cwi.Coordination.Domain.Repositories;

public interface IMeshEventRepository
{
    Task AppendAsync(MeshEvent meshEvent, CancellationToken cancellationToken);
    Task<IReadOnlyList<MeshEvent>> ListAsync(int limit, DateTimeOffset? sinceUtc, CancellationToken cancellationToken);
    Task PruneAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken);
}
