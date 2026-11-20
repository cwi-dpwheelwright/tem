using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class CanonicalRecordRepository : ICanonicalRecordRepository
{
    private readonly OrchestratorDbContext _db;

    public CanonicalRecordRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public Task<CanonicalRecord?> GetAsync(string recordId, CancellationToken cancellationToken)
    {
        return _db.CanonicalRecords
            .FirstOrDefaultAsync(record => record.RecordId == recordId, cancellationToken);
    }

    public async Task<CanonicalRecord> UpsertAsync(CanonicalRecord record, CancellationToken cancellationToken)
    {
        var existing = await _db.CanonicalRecords
            .FirstOrDefaultAsync(item => item.RecordId == record.RecordId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _db.CanonicalRecords.Add(record);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return record;
        }

        existing.CanonicalJson = record.CanonicalJson;
        existing.SourcesJson = record.SourcesJson;
        existing.ConflictsJson = record.ConflictsJson;
        existing.UpdatedUtc = record.UpdatedUtc;

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return existing;
    }
}
