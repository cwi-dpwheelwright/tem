using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface ISuccessionPlanRepository
{
    Task<SuccessionPlan?> GetAsync(string tenantId, string planId, CancellationToken cancellationToken);
    Task AddAsync(SuccessionPlan plan, CancellationToken cancellationToken);
    Task UpdateAsync(SuccessionPlan plan, CancellationToken cancellationToken);
    Task AddSnapshotAsync(SuccessionSnapshot snapshot, CancellationToken cancellationToken);
    Task<SuccessionSnapshot?> GetSnapshotAsync(string tenantId, string snapshotId, CancellationToken cancellationToken);
}
