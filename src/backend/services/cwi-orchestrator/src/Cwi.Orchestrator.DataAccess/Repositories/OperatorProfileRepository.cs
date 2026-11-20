using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class OperatorProfileRepository : IOperatorProfileRepository
{
    private readonly OrchestratorDbContext _db;

    public OperatorProfileRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public Task<OperatorProfile?> GetAsync(string tenantId, string operatorId, CancellationToken cancellationToken)
    {
        return _db.OperatorProfiles
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId && x.OperatorId == operatorId,
                cancellationToken);
    }

    public async Task UpsertAsync(OperatorProfile profile, CancellationToken cancellationToken)
    {
        var existing = await _db.OperatorProfiles
            .FirstOrDefaultAsync(
                x => x.TenantId == profile.TenantId && x.OperatorId == profile.OperatorId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _db.OperatorProfiles.Add(profile);
        }
        else
        {
            existing.SignalsTotal = profile.SignalsTotal;
            existing.CorrectionsTotal = profile.CorrectionsTotal;
            existing.DelaysTotal = profile.DelaysTotal;
            existing.ConfirmationsTotal = profile.ConfirmationsTotal;
            existing.LatencySum = profile.LatencySum;
            existing.LatencyCount = profile.LatencyCount;
            existing.CorrelationScore = profile.CorrelationScore;
            existing.LastUpdatedUtc = profile.LastUpdatedUtc;
            _db.OperatorProfiles.Update(existing);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
