using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class BreakGlassTokenRepository : IBreakGlassTokenRepository
{
    private readonly OrchestratorDbContext _db;

    public BreakGlassTokenRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(BreakGlassToken token, CancellationToken cancellationToken)
    {
        _db.BreakGlassTokens.Add(token);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<BreakGlassToken?> GetByTokenIdAsync(string tokenId, CancellationToken cancellationToken)
    {
        return await _db.BreakGlassTokens
            .FirstOrDefaultAsync(t => t.TokenId == tokenId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RevokeAsync(BreakGlassToken token, CancellationToken cancellationToken)
    {
        _db.BreakGlassTokens.Update(token);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> GetCountAsync(string tenantId, CancellationToken cancellationToken)
    {
        return await _db.BreakGlassTokens
            .Where(t => t.TenantId == tenantId)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BreakGlassToken>> GetByTenantAsync(
        string tenantId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        return await _db.BreakGlassTokens
            .Where(t => t.TenantId == tenantId)
            .OrderByDescending(t => t.CreatedUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
