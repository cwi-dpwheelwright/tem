using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class HomeNodePolicyScopeRepository : IHomeNodePolicyScopeRepository
{
    private readonly OrchestratorDbContext _db;

    public HomeNodePolicyScopeRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public Task<HomeNodePolicyScope?> GetAsync(
        string tenantId,
        string policyScopeId,
        CancellationToken cancellationToken)
    {
        return _db.HomeNodePolicyScopes.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.PolicyScopeId == policyScopeId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<HomeNodePolicyScope>> ListAsync(string tenantId, CancellationToken cancellationToken)
    {
        return await _db.HomeNodePolicyScopes
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.PolicyScopeId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(HomeNodePolicyScope scope, CancellationToken cancellationToken)
    {
        var existing = await _db.HomeNodePolicyScopes.FirstOrDefaultAsync(
            x => x.TenantId == scope.TenantId && x.PolicyScopeId == scope.PolicyScopeId,
            cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            _db.HomeNodePolicyScopes.Add(scope);
        }
        else
        {
            existing.RulesJson = scope.RulesJson;
            existing.Version = scope.Version;
            existing.UpdatedUtc = scope.UpdatedUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
