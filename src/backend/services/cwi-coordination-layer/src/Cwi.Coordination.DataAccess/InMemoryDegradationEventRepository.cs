using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.DataAccess;

public sealed class InMemoryDegradationEventRepository : IDegradationEventRepository
{
    private readonly List<DegradationEvent> _events = new();
    private readonly object _lock = new();

    public Task AppendAsync(DegradationEvent degradationEvent, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _events.Add(degradationEvent);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DegradationEvent>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        IReadOnlyList<DegradationEvent> snapshot;
        lock (_lock)
        {
            snapshot = _events
                .OrderByDescending(e => e.ReceivedUtc)
                .Take(Math.Max(limit, 0))
                .ToList();
        }
        return Task.FromResult(snapshot);
    }
}
