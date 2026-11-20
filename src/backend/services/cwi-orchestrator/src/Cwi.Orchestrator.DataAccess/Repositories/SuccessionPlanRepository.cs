using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class SuccessionPlanRepository : ISuccessionPlanRepository
{
    private readonly OrchestratorDbContext _db;

    public SuccessionPlanRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public Task<SuccessionPlan?> GetAsync(string tenantId, string planId, CancellationToken cancellationToken)
    {
        return _db.SuccessionPlans.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.PlanId == planId,
            cancellationToken);
    }

    public async Task AddAsync(SuccessionPlan plan, CancellationToken cancellationToken)
    {
        _db.SuccessionPlans.Add(plan);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(SuccessionPlan plan, CancellationToken cancellationToken)
    {
        var existing = await _db.SuccessionPlans.FirstOrDefaultAsync(
            x => x.TenantId == plan.TenantId && x.PlanId == plan.PlanId,
            cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            _db.SuccessionPlans.Add(plan);
        }
        else
        {
            existing.AssetId = plan.AssetId;
            existing.CurrentOwnerId = plan.CurrentOwnerId;
            existing.Status = plan.Status;
            existing.SuccessorsJson = plan.SuccessorsJson;
            existing.EffectiveUtc = plan.EffectiveUtc;
            existing.UpdatedUtc = plan.UpdatedUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddSnapshotAsync(SuccessionSnapshot snapshot, CancellationToken cancellationToken)
    {
        _db.SuccessionSnapshots.Add(snapshot);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<SuccessionSnapshot?> GetSnapshotAsync(
        string tenantId,
        string snapshotId,
        CancellationToken cancellationToken)
    {
        return _db.SuccessionSnapshots.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.SnapshotId == snapshotId,
            cancellationToken);
    }
}
