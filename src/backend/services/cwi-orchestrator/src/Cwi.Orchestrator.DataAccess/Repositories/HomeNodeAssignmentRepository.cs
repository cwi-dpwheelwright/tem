using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class HomeNodeAssignmentRepository : IHomeNodeAssignmentRepository
{
    private readonly OrchestratorDbContext _db;

    public HomeNodeAssignmentRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public Task<HomeNodeAssignment?> GetAsync(string tenantId, string userId, CancellationToken cancellationToken)
    {
        return _db.HomeNodeAssignments.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.UserId == userId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<HomeNodeAssignment>> ListAsync(
        string tenantId,
        string? userId,
        CancellationToken cancellationToken)
    {
        var query = _db.HomeNodeAssignments.Where(x => x.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(x => x.UserId == userId);
        }

        return await query
            .OrderBy(x => x.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(HomeNodeAssignment assignment, CancellationToken cancellationToken)
    {
        var existing = await _db.HomeNodeAssignments.FirstOrDefaultAsync(
            x => x.TenantId == assignment.TenantId && x.UserId == assignment.UserId,
            cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            _db.HomeNodeAssignments.Add(assignment);
        }
        else
        {
            existing.PrimaryHomeNodeId = assignment.PrimaryHomeNodeId;
            existing.WatcherNodeId = assignment.WatcherNodeId;
            existing.PolicyScopeId = assignment.PolicyScopeId;
            existing.LocalOnly = assignment.LocalOnly;
            existing.RemoteFallbackAllowed = assignment.RemoteFallbackAllowed;
            existing.Status = assignment.Status;
            existing.AssignedUtc = assignment.AssignedUtc;
            existing.RevokedUtc = assignment.RevokedUtc;
            existing.UpdatedUtc = assignment.UpdatedUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
