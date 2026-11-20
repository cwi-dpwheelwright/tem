using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface ITenantConfigRepository
{
    Task<TenantConfig?> GetAsync(string tenantId, CancellationToken cancellationToken);
    Task UpsertAsync(TenantConfig config, CancellationToken cancellationToken);
}
