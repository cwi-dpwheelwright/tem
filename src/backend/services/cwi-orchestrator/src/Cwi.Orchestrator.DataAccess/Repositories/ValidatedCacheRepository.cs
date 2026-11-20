#nullable enable
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class ValidatedCacheRepository : IValidatedCacheRepository
{
    private readonly OrchestratorDbContext _db;

    public ValidatedCacheRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public Task<ValidatedCacheRecord?> GetAsync(
        string tenantId,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        return _db.ValidatedCacheRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(record =>
                record.TenantId == tenantId
                && record.CacheKey == cacheKey,
                cancellationToken);
    }

    public async Task UpsertAsync(
        ValidatedCacheRecord record,
        CancellationToken cancellationToken)
    {
        var existing = await _db.ValidatedCacheRecords
            .FirstOrDefaultAsync(item =>
                item.TenantId == record.TenantId
                && item.CacheKey == record.CacheKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _db.ValidatedCacheRecords.Add(record);
        }
        else
        {
            existing.Scope = record.Scope;
            existing.UserIdHash = record.UserIdHash;
            existing.SourceProofJson = record.SourceProofJson;
            existing.TraceId = record.TraceId;
            existing.FreshUntilUtc = record.FreshUntilUtc;
            existing.PolicyMode = record.PolicyMode;
            existing.PolicyVersion = record.PolicyVersion;
            existing.PackHash = record.PackHash;
            existing.ValidationStatus = record.ValidationStatus;
            existing.CriticStatus = record.CriticStatus;
            existing.PayloadHash = record.PayloadHash;
            existing.UpdatedUtc = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
#nullable disable
