using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IBreakGlassTokenRepository
{
    Task AddAsync(BreakGlassToken token, CancellationToken cancellationToken);
    Task<BreakGlassToken?> GetByTokenIdAsync(string tokenId, CancellationToken cancellationToken);
    Task RevokeAsync(BreakGlassToken token, CancellationToken cancellationToken);
    Task<int> GetCountAsync(string tenantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<BreakGlassToken>> GetByTenantAsync(string tenantId, int skip, int take, CancellationToken cancellationToken);
}
