using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Coordination.DataAccess.Repositories;

public sealed class SurvivalModeRepository : ISurvivalModeRepository
{
    private readonly CoordinationDbContext _db;

    public SurvivalModeRepository(CoordinationDbContext db)
    {
        _db = db;
    }

    public async Task<SurvivalModeState> GetAsync(CancellationToken cancellationToken)
    {
        var record = await _db.SurvivalModes
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            record = new SurvivalModeRecord
            {
                Id = Guid.NewGuid(),
                Enabled = false,
                Reason = null,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            _db.SurvivalModes.Add(record);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Map(record);
    }

    public async Task<SurvivalModeState> SetAsync(SurvivalModeState state, CancellationToken cancellationToken)
    {
        var record = await _db.SurvivalModes
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            record = new SurvivalModeRecord
            {
                Id = Guid.NewGuid()
            };
            _db.SurvivalModes.Add(record);
        }

        record.Enabled = state.Enabled;
        record.Reason = state.Reason;
        record.UpdatedUtc = state.UpdatedUtc;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Map(record);
    }

    private static SurvivalModeState Map(SurvivalModeRecord record)
    {
        return new SurvivalModeState
        {
            Enabled = record.Enabled,
            Reason = record.Reason,
            UpdatedUtc = record.UpdatedUtc
        };
    }
}
