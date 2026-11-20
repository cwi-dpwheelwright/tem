using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IPackActivationRepository
{
    Task<PackActivationState?> GetAsync(string tenantId, string packName, CancellationToken cancellationToken);
    Task UpsertAsync(PackActivationState state, CancellationToken cancellationToken);
}
