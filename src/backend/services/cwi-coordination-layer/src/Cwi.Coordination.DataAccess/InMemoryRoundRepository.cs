using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.DataAccess;

public sealed class InMemoryRoundRepository : IRoundRepository
{
    private readonly Dictionary<Guid, Round> _rounds = new();

    public Task<Round> AddAsync(Round round, CancellationToken cancellationToken)
    {
        _rounds[round.Id] = round;
        return Task.FromResult(round);
    }

    public Task<IReadOnlyList<Round>> ListAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult((IReadOnlyList<Round>)_rounds.Values.ToList());
    }

    public Task<Round?> GetAsync(Guid roundId, CancellationToken cancellationToken)
    {
        _rounds.TryGetValue(roundId, out var round);
        return Task.FromResult(round);
    }

    public Task UpdateAsync(Round round, CancellationToken cancellationToken)
    {
        _rounds[round.Id] = round;
        return Task.CompletedTask;
    }
}
