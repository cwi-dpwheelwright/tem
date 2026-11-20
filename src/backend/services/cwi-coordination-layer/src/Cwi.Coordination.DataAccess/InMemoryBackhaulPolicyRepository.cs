using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.DataAccess;

public sealed class InMemoryBackhaulPolicyRepository : IBackhaulPolicyRepository
{
    private BackhaulPolicy _policy = new()
    {
        RequireAuthenticatedChannel = true,
        AllowedInterfaces = Array.Empty<InterfaceKind>(),
        DeniedInterfaces = Array.Empty<InterfaceKind>(),
        RevokedPeers = Array.Empty<string>(),
        PolicyVersion = "1.0"
    };

    public Task<BackhaulPolicy> GetAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_policy);
    }

    public Task<BackhaulPolicy> SetAsync(BackhaulPolicy policy, CancellationToken cancellationToken)
    {
        _policy = policy;
        return Task.FromResult(policy);
    }
}
