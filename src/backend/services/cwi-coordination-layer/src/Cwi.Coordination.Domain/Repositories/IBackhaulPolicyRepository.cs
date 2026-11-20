using Cwi.Coordination.Domain.Entities;

namespace Cwi.Coordination.Domain.Repositories;

public interface IBackhaulPolicyRepository
{
    Task<BackhaulPolicy> GetAsync(CancellationToken cancellationToken);
    Task<BackhaulPolicy> SetAsync(BackhaulPolicy policy, CancellationToken cancellationToken);
}
