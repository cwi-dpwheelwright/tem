using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IUserConnectorConnectionRepository
{
    Task<IReadOnlyList<UserConnectorConnection>> ListForUserAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken);

    Task<UserConnectorConnection?> GetForUserAsync(
        string tenantId,
        string userId,
        string connectionId,
        CancellationToken cancellationToken);

    Task UpsertAsync(UserConnectorConnection connection, CancellationToken cancellationToken);
}
