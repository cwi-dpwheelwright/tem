using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IOwnershipLineageRepository
{
    Task AddAsync(OwnershipLineageBinding binding, CancellationToken cancellationToken);
    Task<IReadOnlyList<OwnershipLineageBinding>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken);
}
