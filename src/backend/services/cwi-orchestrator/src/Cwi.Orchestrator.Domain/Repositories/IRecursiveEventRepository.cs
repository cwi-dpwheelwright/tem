#nullable enable
using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IRecursiveEventRepository
{
    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken);
    Task AddAsync(RecursiveEventRecord record, CancellationToken cancellationToken);
    Task AddQuarantineAsync(RecursiveEventQuarantineRecord record, CancellationToken cancellationToken);
    Task<IReadOnlyList<RecursiveEventRecord>> QueryAsync(
        string? tenantId,
        string? eventType,
        string? temporalState,
        int skip,
        int take,
        CancellationToken cancellationToken);
}
#nullable disable
