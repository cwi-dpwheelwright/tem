#nullable enable
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class RecursiveEventRepository : IRecursiveEventRepository
{
    private readonly OrchestratorDbContext _db;

    public RecursiveEventRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken)
    {
        return _db.RecursiveEvents.AnyAsync(evt => evt.EventId == eventId, cancellationToken);
    }

    public async Task AddAsync(RecursiveEventRecord record, CancellationToken cancellationToken)
    {
        _db.RecursiveEvents.Add(record);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddQuarantineAsync(RecursiveEventQuarantineRecord record, CancellationToken cancellationToken)
    {
        _db.RecursiveEventQuarantine.Add(record);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RecursiveEventRecord>> QueryAsync(
        string? tenantId,
        string? eventType,
        string? temporalState,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = _db.RecursiveEvents.AsQueryable();

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(evt => evt.TenantId == tenantId);
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(evt => evt.EventType == eventType);
        }

        if (!string.IsNullOrWhiteSpace(temporalState))
        {
            query = query.Where(evt => evt.TemporalState == temporalState);
        }

        return await query
            .OrderByDescending(evt => evt.TimestampUtc)
            .ThenByDescending(evt => evt.CreatedUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
#nullable disable
