using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class HomeNodeRepository : IHomeNodeRepository
{
    private readonly OrchestratorDbContext _db;

    public HomeNodeRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public Task<HomeNodeRecord?> GetAsync(string tenantId, string nodeId, CancellationToken cancellationToken)
    {
        return _db.HomeNodeRecords.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.NodeId == nodeId,
            cancellationToken);
    }

    public Task<HomeNodeRecord?> FindByNodeIdAsync(string nodeId, CancellationToken cancellationToken)
    {
        return _db.HomeNodeRecords.FirstOrDefaultAsync(
            x => x.NodeId == nodeId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<HomeNodeRecord>> ListAsync(
        string tenantId,
        string? ownerUserId,
        string? status,
        CancellationToken cancellationToken)
    {
        var query = _db.HomeNodeRecords.Where(x => x.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(ownerUserId))
        {
            query = query.Where(x => x.OwnerUserId == ownerUserId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status);
        }

        return await query
            .OrderBy(x => x.NodeId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(HomeNodeRecord record, CancellationToken cancellationToken)
    {
        var existing = await _db.HomeNodeRecords.FirstOrDefaultAsync(
            x => x.TenantId == record.TenantId && x.NodeId == record.NodeId,
            cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            _db.HomeNodeRecords.Add(record);
        }
        else
        {
            existing.OwnerUserId = record.OwnerUserId;
            existing.DeviceClass = record.DeviceClass;
            existing.TailscaleIp = record.TailscaleIp;
            existing.WatcherNodeId = record.WatcherNodeId;
            existing.CapabilitiesJson = record.CapabilitiesJson;
            existing.PackVersionsJson = record.PackVersionsJson;
            existing.PolicyProfileVersion = record.PolicyProfileVersion;
            existing.Status = record.Status;
            existing.LastHeartbeatUtc = record.LastHeartbeatUtc;
            existing.AssignedUtc = record.AssignedUtc;
            existing.UpdatedUtc = record.UpdatedUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
