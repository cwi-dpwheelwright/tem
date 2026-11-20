using Cwi.Coordination.Domain.Entities;

namespace Cwi.Coordination.Domain.Repositories;

public interface INodeWorkRepository
{
    Task<NodeWorkItem> AddAsync(NodeWorkItem item, CancellationToken cancellationToken);
    Task<NodeWorkItem?> GetAsync(string workId, CancellationToken cancellationToken);
    Task<NodeWorkItem?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<NodeWorkItem>> ListAsync(
        string? tenantId,
        string? nodeId,
        string? status,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<NodeWorkItem>> ListLeasableAsync(
        string nodeId,
        DateTimeOffset nowUtc,
        int take,
        CancellationToken cancellationToken);
    Task<NodeWorkItem> UpdateAsync(NodeWorkItem item, CancellationToken cancellationToken);
}
