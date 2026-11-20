using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.DataAccess;

public sealed class InMemoryMeshEventRepository : IMeshEventRepository
{
    private readonly List<MeshEvent> _events = new();

    public Task AppendAsync(MeshEvent meshEvent, CancellationToken cancellationToken)
    {
        _events.Add(meshEvent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MeshEvent>> ListAsync(
        int limit,
        DateTimeOffset? sinceUtc,
        CancellationToken cancellationToken)
    {
        var query = _events.AsEnumerable();
        if (sinceUtc.HasValue)
        {
            query = query.Where(evt => evt.CreatedUtc >= sinceUtc.Value);
        }

        var items = query
            .OrderByDescending(evt => evt.CreatedUtc)
            .Take(limit)
            .ToList();
        return Task.FromResult((IReadOnlyList<MeshEvent>)items);
    }

    public Task PruneAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        _events.RemoveAll(evt => evt.CreatedUtc < cutoffUtc);
        return Task.CompletedTask;
    }
}
