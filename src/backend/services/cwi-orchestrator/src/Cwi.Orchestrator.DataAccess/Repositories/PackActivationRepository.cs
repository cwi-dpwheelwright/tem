using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class PackActivationRepository : IPackActivationRepository
{
    private readonly OrchestratorDbContext _db;

    public PackActivationRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public Task<PackActivationState?> GetAsync(string tenantId, string packName, CancellationToken cancellationToken)
    {
        return _db.PackActivationStates
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PackName == packName, cancellationToken);
    }

    public async Task UpsertAsync(PackActivationState state, CancellationToken cancellationToken)
    {
        var exists = await _db.PackActivationStates.AnyAsync(
            x => x.TenantId == state.TenantId && x.PackName == state.PackName,
            cancellationToken).ConfigureAwait(false);

        if (exists)
        {
            _db.PackActivationStates.Update(state);
        }
        else
        {
            _db.PackActivationStates.Add(state);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
