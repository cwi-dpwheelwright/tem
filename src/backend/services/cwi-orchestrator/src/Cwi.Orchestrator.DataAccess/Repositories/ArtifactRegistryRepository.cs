using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class ArtifactRegistryRepository : IArtifactRegistryRepository
{
    private readonly OrchestratorDbContext _db;

    public ArtifactRegistryRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public Task<ArtifactRecord?> GetAsync(string tenantId, string artifactId, CancellationToken cancellationToken)
    {
        return _db.ArtifactRecords.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.ArtifactId == artifactId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<ArtifactRecord>> ListAsync(
        string tenantId,
        string? artifactType,
        CancellationToken cancellationToken)
    {
        var query = _db.ArtifactRecords.Where(x => x.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(artifactType))
        {
            query = query.Where(x => x.ArtifactType == artifactType);
        }

        return await query
            .OrderBy(x => x.ArtifactId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpsertAsync(ArtifactRecord record, CancellationToken cancellationToken)
    {
        var existing = await _db.ArtifactRecords.FirstOrDefaultAsync(
            x => x.TenantId == record.TenantId && x.ArtifactId == record.ArtifactId,
            cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            _db.ArtifactRecords.Add(record);
        }
        else
        {
            existing.ArtifactType = record.ArtifactType;
            existing.Title = record.Title;
            existing.Status = record.Status;
            existing.VersionsJson = record.VersionsJson;
            existing.UpdatedUtc = record.UpdatedUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
