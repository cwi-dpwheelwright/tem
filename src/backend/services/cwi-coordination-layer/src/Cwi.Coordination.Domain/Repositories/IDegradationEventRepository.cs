using Cwi.Coordination.Domain.Entities;

namespace Cwi.Coordination.Domain.Repositories;

public interface IDegradationEventRepository
{
    Task AppendAsync(DegradationEvent degradationEvent, CancellationToken cancellationToken);
    Task<IReadOnlyList<DegradationEvent>> ListAsync(int limit, CancellationToken cancellationToken);
}
