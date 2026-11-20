using Cwi.Coordination.Domain.Entities;

namespace Cwi.Coordination.Domain.Repositories;

public interface IRoundRepository
{
    Task<Round> AddAsync(Round round, CancellationToken cancellationToken);
    Task<IReadOnlyList<Round>> ListAsync(CancellationToken cancellationToken);
    Task<Round?> GetAsync(Guid roundId, CancellationToken cancellationToken);
    Task UpdateAsync(Round round, CancellationToken cancellationToken);
}
