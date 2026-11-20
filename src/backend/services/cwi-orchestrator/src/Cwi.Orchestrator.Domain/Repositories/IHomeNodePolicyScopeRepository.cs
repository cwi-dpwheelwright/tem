using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IHomeNodePolicyScopeRepository
{
    Task<HomeNodePolicyScope?> GetAsync(string tenantId, string policyScopeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<HomeNodePolicyScope>> ListAsync(string tenantId, CancellationToken cancellationToken);
    Task UpsertAsync(HomeNodePolicyScope scope, CancellationToken cancellationToken);
}
