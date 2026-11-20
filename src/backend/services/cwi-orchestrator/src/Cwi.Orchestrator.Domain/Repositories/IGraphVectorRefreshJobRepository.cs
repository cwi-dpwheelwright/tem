#nullable enable
using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IGraphVectorRefreshJobRepository
{
    Task<bool> ExistsForGraphCandidateAsync(
        string graphCandidateId,
        CancellationToken cancellationToken);

    Task AddAsync(
        GraphVectorRefreshJobRecord record,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GraphVectorRefreshJobRecord>> QueryAsync(
        string? tenantId,
        string? refreshStatus,
        string? graphCandidateId,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<GraphVectorRefreshJobRecord?> GetByJobIdAsync(
        string jobId,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        GraphVectorRefreshJobRecord record,
        CancellationToken cancellationToken);
}
#nullable disable
