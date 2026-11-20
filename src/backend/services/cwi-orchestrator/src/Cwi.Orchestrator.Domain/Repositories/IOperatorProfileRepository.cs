using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IOperatorProfileRepository
{
    Task<OperatorProfile?> GetAsync(string tenantId, string operatorId, CancellationToken cancellationToken);
    Task UpsertAsync(OperatorProfile profile, CancellationToken cancellationToken);
}
