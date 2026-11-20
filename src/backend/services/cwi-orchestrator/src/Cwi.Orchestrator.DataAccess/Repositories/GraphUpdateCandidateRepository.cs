#nullable enable
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class GraphUpdateCandidateRepository : IGraphUpdateCandidateRepository
{
    private readonly OrchestratorDbContext _db;

    public GraphUpdateCandidateRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsForRecursiveEventAsync(
        string recursiveEventId,
        CancellationToken cancellationToken)
    {
        return _db.GraphUpdateCandidates
            .AnyAsync(candidate => candidate.RecursiveEventId == recursiveEventId, cancellationToken);
    }

    public async Task AddAsync(
        GraphUpdateCandidateRecord record,
        CancellationToken cancellationToken)
    {
        _db.GraphUpdateCandidates.Add(record);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GraphUpdateCandidateRecord>> QueryAsync(
        string? tenantId,
        string? candidateState,
        string? temporalState,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _db.GraphUpdateCandidates.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(candidate => candidate.TenantId == tenantId);
        }

        if (!string.IsNullOrWhiteSpace(candidateState))
        {
            query = query.Where(candidate => candidate.CandidateState == candidateState);
        }

        if (!string.IsNullOrWhiteSpace(temporalState))
        {
            query = query.Where(candidate => candidate.TemporalState == temporalState);
        }

        return await query
            .OrderByDescending(candidate => candidate.ObservedUtc)
            .ThenByDescending(candidate => candidate.CreatedUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<GraphUpdateCandidateRecord?> GetByCandidateIdAsync(
        string candidateId,
        CancellationToken cancellationToken)
    {
        return _db.GraphUpdateCandidates
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.CandidateId == candidateId, cancellationToken);
    }
}
#nullable disable
