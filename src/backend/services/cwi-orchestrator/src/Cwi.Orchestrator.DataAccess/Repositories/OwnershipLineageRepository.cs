using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class OwnershipLineageRepository : IOwnershipLineageRepository
{
    private readonly OrchestratorDbContext _db;

    public OwnershipLineageRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(OwnershipLineageBinding binding, CancellationToken cancellationToken)
    {
        _db.OwnershipLineageBindings.Add(binding);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OwnershipLineageBinding>> ListByTenantAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        return await _db.OwnershipLineageBindings
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
