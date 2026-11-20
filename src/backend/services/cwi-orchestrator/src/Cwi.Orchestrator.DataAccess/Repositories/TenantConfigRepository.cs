using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class TenantConfigRepository : ITenantConfigRepository
{
    private readonly OrchestratorDbContext _db;

    public TenantConfigRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public Task<TenantConfig?> GetAsync(string tenantId, CancellationToken cancellationToken)
    {
        return _db.TenantConfigs.FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
    }

    public async Task UpsertAsync(TenantConfig config, CancellationToken cancellationToken)
    {
        var existing = await _db.TenantConfigs.FirstOrDefaultAsync(
            x => x.TenantId == config.TenantId,
            cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            _db.TenantConfigs.Add(config);
        }
        else
        {
            existing.ConfigJson = config.ConfigJson;
            existing.UpdatedUtc = config.UpdatedUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
