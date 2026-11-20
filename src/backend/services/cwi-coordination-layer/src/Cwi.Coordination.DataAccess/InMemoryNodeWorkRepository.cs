using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.DataAccess;

public sealed class InMemoryNodeWorkRepository : INodeWorkRepository
{
    private readonly Dictionary<string, NodeWorkItem> _items = new(StringComparer.OrdinalIgnoreCase);

    public Task<NodeWorkItem> AddAsync(NodeWorkItem item, CancellationToken cancellationToken)
    {
        _items[item.WorkId] = item;
        return Task.FromResult(item);
    }

    public Task<NodeWorkItem?> GetAsync(string workId, CancellationToken cancellationToken)
    {
        _items.TryGetValue(workId, out var item);
        return Task.FromResult(item);
    }

    public Task<NodeWorkItem?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        var item = _items.Values.FirstOrDefault(x => x.IdempotencyKey == idempotencyKey);
        return Task.FromResult(item);
    }

    public Task<IReadOnlyList<NodeWorkItem>> ListAsync(
        string? tenantId,
        string? nodeId,
        string? status,
        CancellationToken cancellationToken)
    {
        var query = _items.Values.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(x => x.TenantId == tenantId);
        }

        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            query = query.Where(x => x.NodeId == nodeId);
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<NodeWorkStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(x => x.Status == parsedStatus);
        }

        return Task.FromResult<IReadOnlyList<NodeWorkItem>>(query.OrderBy(x => x.CreatedUtc).ToList());
    }

    public Task<IReadOnlyList<NodeWorkItem>> ListLeasableAsync(
        string nodeId,
        DateTimeOffset nowUtc,
        int take,
        CancellationToken cancellationToken)
    {
        var items = _items.Values
            .Where(x => x.NodeId == nodeId)
            .Where(x => x.AvailableUtc <= nowUtc)
            .Where(x => x.ExpiresUtc == null || x.ExpiresUtc > nowUtc)
            .Where(x =>
                x.Status == NodeWorkStatus.Queued ||
                ((x.Status == NodeWorkStatus.Leased || x.Status == NodeWorkStatus.Acknowledged) &&
                 x.LeaseExpiresUtc != null &&
                 x.LeaseExpiresUtc <= nowUtc))
            .OrderBy(x => x.AvailableUtc)
            .ThenBy(x => x.CreatedUtc)
            .Take(take)
            .ToList();

        return Task.FromResult<IReadOnlyList<NodeWorkItem>>(items);
    }

    public Task<NodeWorkItem> UpdateAsync(NodeWorkItem item, CancellationToken cancellationToken)
    {
        _items[item.WorkId] = item;
        return Task.FromResult(item);
    }
}
