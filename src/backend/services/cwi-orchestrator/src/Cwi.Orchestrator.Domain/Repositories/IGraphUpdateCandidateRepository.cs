#nullable enable
using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IGraphUpdateCandidateRepository
{
    Task<bool> ExistsForRecursiveEventAsync(
        string recursiveEventId,
        CancellationToken cancellationToken);

    Task AddAsync(
        GraphUpdateCandidateRecord record,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GraphUpdateCandidateRecord>> QueryAsync(
        string? tenantId,
        string? candidateState,
        string? temporalState,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<GraphUpdateCandidateRecord?> GetByCandidateIdAsync(
        string candidateId,
        CancellationToken cancellationToken);
}
#nullable disable
