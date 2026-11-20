using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IHomeNodeAssignmentRepository
{
    Task<HomeNodeAssignment?> GetAsync(string tenantId, string userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<HomeNodeAssignment>> ListAsync(
        string tenantId,
        string? userId,
        CancellationToken cancellationToken);
    Task UpsertAsync(HomeNodeAssignment assignment, CancellationToken cancellationToken);
}
