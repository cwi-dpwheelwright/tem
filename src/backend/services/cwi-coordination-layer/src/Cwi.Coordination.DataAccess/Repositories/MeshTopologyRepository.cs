using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Coordination.DataAccess.Repositories;

public sealed class MeshTopologyRepository : IMeshTopologyRepository
{
    private readonly CoordinationDbContext _db;

    public MeshTopologyRepository(CoordinationDbContext db)
    {
        _db = db;
    }

    public async Task UpsertAsync(LinkState state, CancellationToken cancellationToken)
    {
        var existing = await _db.LinkStates.FirstOrDefaultAsync(
            x => x.NodeId == state.NodeId && x.Interface == state.Interface,
            cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            _db.LinkStates.Add(state);
        }
        else
        {
            existing.SignalQuality = state.SignalQuality;
            existing.LatencyMs = state.LatencyMs;
            existing.JitterMs = state.JitterMs;
            existing.PacketLoss = state.PacketLoss;
            existing.BandwidthKbps = state.BandwidthKbps;
            existing.ThroughputKbps = state.ThroughputKbps;
            existing.RssiDbm = state.RssiDbm;
            existing.SnrDb = state.SnrDb;
            existing.Status = state.Status;
            existing.UpdatedUtc = state.UpdatedUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<LinkState?> GetAsync(string nodeId, InterfaceKind interfaceKind, CancellationToken cancellationToken)
    {
        return _db.LinkStates.FirstOrDefaultAsync(
            x => x.NodeId == nodeId && x.Interface == interfaceKind,
            cancellationToken);
    }

    public async Task<IReadOnlyList<LinkState>> ListAsync(CancellationToken cancellationToken)
    {
        return await _db.LinkStates
            .OrderByDescending(x => x.UpdatedUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LinkState>> ListByNodeAsync(string nodeId, CancellationToken cancellationToken)
    {
        return await _db.LinkStates
            .Where(x => x.NodeId == nodeId)
            .OrderByDescending(x => x.UpdatedUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
