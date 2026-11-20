using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Coordination.DataAccess.Repositories;

public sealed class MembershipRepository : IMembershipRepository
{
    private readonly CoordinationDbContext _db;

    public MembershipRepository(CoordinationDbContext db)
    {
        _db = db;
    }

    public async Task<Member> AddAsync(Member member, CancellationToken cancellationToken)
    {
        _db.Members.Add(member);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return member;
    }

    public Task<Member?> GetAsync(string nodeId, CancellationToken cancellationToken)
    {
        return _db.Members.FirstOrDefaultAsync(
            x => x.NodeId == nodeId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Member>> ListAsync(CancellationToken cancellationToken)
    {
        return await _db.Members
            .OrderBy(x => x.NodeId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Member> UpdateAsync(Member member, CancellationToken cancellationToken)
    {
        var existing = await _db.Members.FirstOrDefaultAsync(
            x => x.NodeId == member.NodeId,
            cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            _db.Members.Add(member);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return member;
        }

        existing.TenantId = member.TenantId;
        existing.OwnerUserId = member.OwnerUserId;
        existing.DeviceClass = member.DeviceClass;
        existing.TailscaleIp = member.TailscaleIp;
        existing.WatcherNodeId = member.WatcherNodeId;
        existing.CapabilitiesJson = member.CapabilitiesJson;
        existing.PackVersionsJson = member.PackVersionsJson;
        existing.PolicyProfileVersion = member.PolicyProfileVersion;
        existing.HealthJson = member.HealthJson;
        existing.CertificateFingerprint = member.CertificateFingerprint;
        existing.Status = member.Status;
        existing.LastSeenUtc = member.LastSeenUtc;
        existing.RevokedUtc = member.RevokedUtc;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return existing;
    }
}
