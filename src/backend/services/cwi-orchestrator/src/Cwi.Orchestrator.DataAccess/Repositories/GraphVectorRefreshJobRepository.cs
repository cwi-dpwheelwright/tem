#nullable enable
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class GraphVectorRefreshJobRepository : IGraphVectorRefreshJobRepository
{
    private readonly OrchestratorDbContext _db;

    public GraphVectorRefreshJobRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsForGraphCandidateAsync(
        string graphCandidateId,
        CancellationToken cancellationToken)
    {
        return _db.GraphVectorRefreshJobs
            .AnyAsync(job => job.GraphCandidateId == graphCandidateId, cancellationToken);
    }

    public async Task AddAsync(
        GraphVectorRefreshJobRecord record,
        CancellationToken cancellationToken)
    {
        _db.GraphVectorRefreshJobs.Add(record);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GraphVectorRefreshJobRecord>> QueryAsync(
        string? tenantId,
        string? refreshStatus,
        string? graphCandidateId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _db.GraphVectorRefreshJobs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(job => job.TenantId == tenantId);
        }

        if (!string.IsNullOrWhiteSpace(refreshStatus))
        {
            query = query.Where(job => job.RefreshStatus == refreshStatus);
        }

        if (!string.IsNullOrWhiteSpace(graphCandidateId))
        {
            query = query.Where(job => job.GraphCandidateId == graphCandidateId);
        }

        return await query
            .OrderByDescending(job => job.UpdatedUtc)
            .ThenByDescending(job => job.CreatedUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<GraphVectorRefreshJobRecord?> GetByJobIdAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        return _db.GraphVectorRefreshJobs
            .FirstOrDefaultAsync(job => job.JobId == jobId, cancellationToken);
    }

    public async Task UpdateAsync(
        GraphVectorRefreshJobRecord record,
        CancellationToken cancellationToken)
    {
        _db.GraphVectorRefreshJobs.Update(record);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
#nullable disable
