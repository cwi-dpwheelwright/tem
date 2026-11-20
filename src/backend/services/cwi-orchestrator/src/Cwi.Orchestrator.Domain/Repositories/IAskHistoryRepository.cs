#nullable enable
using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IAskHistoryRepository
{
    Task AddAsync(AskHistoryRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<AskHistoryRecord>> ListAsync(
        string tenantId,
        string userId,
        int limit,
        DateTimeOffset? createdAfter,
        CancellationToken cancellationToken);

    Task<int> DeleteOlderThanAsync(
        string tenantId,
        string userId,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken);

    Task<int> DeleteAllAsync(string tenantId, string userId, CancellationToken cancellationToken);

    Task<int> TrimToLatestAsync(
        string tenantId,
        string userId,
        int maxEntries,
        CancellationToken cancellationToken);
}
#nullable disable
