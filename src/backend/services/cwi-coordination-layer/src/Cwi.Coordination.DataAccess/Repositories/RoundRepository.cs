using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Coordination.DataAccess.Repositories;

public sealed class RoundRepository : IRoundRepository
{
    private readonly CoordinationDbContext _db;

    public RoundRepository(CoordinationDbContext db)
    {
        _db = db;
    }

    public async Task<Round> AddAsync(Round round, CancellationToken cancellationToken)
    {
        _db.Rounds.Add(round);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return round;
    }

    public async Task<IReadOnlyList<Round>> ListAsync(CancellationToken cancellationToken)
    {
        return await _db.Rounds
            .OrderByDescending(x => x.CreatedUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<Round?> GetAsync(Guid roundId, CancellationToken cancellationToken)
    {
        return _db.Rounds.FirstOrDefaultAsync(x => x.Id == roundId, cancellationToken);
    }

    public async Task UpdateAsync(Round round, CancellationToken cancellationToken)
    {
        var existing = await _db.Rounds.FirstOrDefaultAsync(
            x => x.Id == round.Id,
            cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            _db.Rounds.Add(round);
        }
        else
        {
            existing.Status = round.Status;
            existing.ClosedUtc = round.ClosedUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
