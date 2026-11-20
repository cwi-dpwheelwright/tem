using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IHomeNodeRepository
{
    Task<HomeNodeRecord?> GetAsync(string tenantId, string nodeId, CancellationToken cancellationToken);
    Task<HomeNodeRecord?> FindByNodeIdAsync(string nodeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<HomeNodeRecord>> ListAsync(
        string tenantId,
        string? ownerUserId,
        string? status,
        CancellationToken cancellationToken);
    Task UpsertAsync(HomeNodeRecord record, CancellationToken cancellationToken);
}
