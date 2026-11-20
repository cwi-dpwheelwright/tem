using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class JourneyEventRepository : IJourneyEventRepository
{
    private readonly OrchestratorDbContext _db;

    public JourneyEventRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public Task<string?> GetLatestStreamIdAsync(CancellationToken cancellationToken)
    {
        return _db.JourneyEventRecords
            .OrderByDescending(x => x.Id)
            .Select(x => x.StreamId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> ExistsStreamIdAsync(string streamId, CancellationToken cancellationToken)
    {
        return _db.JourneyEventRecords.AnyAsync(x => x.StreamId == streamId, cancellationToken);
    }

    public async Task AddAsync(JourneyEventRecord record, CancellationToken cancellationToken)
    {
        _db.JourneyEventRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JourneyEventRecord>> ListByRequestIdAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        return await _db.JourneyEventRecords
            .Where(x => x.RequestId == requestId)
            .OrderBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
