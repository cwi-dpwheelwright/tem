using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Coordination.DataAccess.Repositories;

public sealed class NodeWorkRepository : INodeWorkRepository
{
    private readonly CoordinationDbContext _db;

    public NodeWorkRepository(CoordinationDbContext db)
    {
        _db = db;
    }

    public async Task<NodeWorkItem> AddAsync(NodeWorkItem item, CancellationToken cancellationToken)
    {
        _db.NodeWorkItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return item;
    }

    public Task<NodeWorkItem?> GetAsync(string workId, CancellationToken cancellationToken)
    {
        return _db.NodeWorkItems.FirstOrDefaultAsync(x => x.WorkId == workId, cancellationToken);
    }

    public Task<NodeWorkItem?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        return _db.NodeWorkItems.FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public async Task<IReadOnlyList<NodeWorkItem>> ListAsync(
        string? tenantId,
        string? nodeId,
        string? status,
        CancellationToken cancellationToken)
    {
        var query = _db.NodeWorkItems.AsQueryable();

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

        return await query
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<NodeWorkItem>> ListLeasableAsync(
        string nodeId,
        DateTimeOffset nowUtc,
        int take,
        CancellationToken cancellationToken)
    {
        return await _db.NodeWorkItems
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
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<NodeWorkItem> UpdateAsync(NodeWorkItem item, CancellationToken cancellationToken)
    {
        var existing = await _db.NodeWorkItems.FirstOrDefaultAsync(x => x.WorkId == item.WorkId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _db.NodeWorkItems.Add(item);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return item;
        }

        existing.NodeId = item.NodeId;
        existing.RequestId = item.RequestId;
        existing.TraceId = item.TraceId;
        existing.TenantId = item.TenantId;
        existing.UserId = item.UserId;
        existing.TaskType = item.TaskType;
        existing.PolicyScope = item.PolicyScope;
        existing.PayloadJson = item.PayloadJson;
        existing.WorkToken = item.WorkToken;
        existing.IdempotencyKey = item.IdempotencyKey;
        existing.Status = item.Status;
        existing.AvailableUtc = item.AvailableUtc;
        existing.ExpiresUtc = item.ExpiresUtc;
        existing.LeaseId = item.LeaseId;
        existing.LeaseExpiresUtc = item.LeaseExpiresUtc;
        existing.LeaseCount = item.LeaseCount;
        existing.ResultJson = item.ResultJson;
        existing.CompletedUtc = item.CompletedUtc;
        existing.UpdatedUtc = item.UpdatedUtc;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return existing;
    }
}
