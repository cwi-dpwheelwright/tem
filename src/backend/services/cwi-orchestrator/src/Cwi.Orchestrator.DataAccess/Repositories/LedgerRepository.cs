using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class LedgerRepository : ILedgerRepository
{
    private readonly OrchestratorDbContext _db;

    public LedgerRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public async Task<string?> GetLatestHashAsync(string traceId, CancellationToken cancellationToken)
    {
        return await _db.LedgerEvents
            .Where(e => e.TraceId == traceId)
            .OrderByDescending(e => e.CreatedUtc)
            .Select(e => e.Hash)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LedgerEvent>> GetByTraceIdAsync(
        string traceId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        return await _db.LedgerEvents
            .Where(e => e.TraceId == traceId)
            .OrderBy(e => e.CreatedUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> GetCountAsync(string traceId, CancellationToken cancellationToken)
    {
        return await _db.LedgerEvents
            .Where(e => e.TraceId == traceId)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LedgerEvent>> SearchAsync(
        DateTimeOffset? startUtc,
        DateTimeOffset? endUtc,
        string? eventType,
        CancellationToken cancellationToken)
    {
        var query = _db.LedgerEvents.AsQueryable();

        if (startUtc.HasValue)
        {
            query = query.Where(e => e.CreatedUtc >= startUtc.Value);
        }

        if (endUtc.HasValue)
        {
            query = query.Where(e => e.CreatedUtc <= endUtc.Value);
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(e => e.EventType == eventType);
        }

        return await query
            .OrderByDescending(e => e.CreatedUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
    {
        _db.LedgerEvents.Add(ledgerEvent);
        _db.LedgerOutboxEntries.Add(new Cwi.Orchestrator.Domain.Entities.LedgerOutboxEntry
        {
            Id = Guid.NewGuid(),
            TraceId = ledgerEvent.TraceId,
            EventType = ledgerEvent.EventType,
            PayloadJson = ledgerEvent.PayloadJson,
            PrevHash = ledgerEvent.PrevHash,
            Hash = ledgerEvent.Hash,
            HashAlgorithm = ledgerEvent.HashAlgorithm,
            CreatedUtc = ledgerEvent.CreatedUtc,
            PublishedUtc = null
        });
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
