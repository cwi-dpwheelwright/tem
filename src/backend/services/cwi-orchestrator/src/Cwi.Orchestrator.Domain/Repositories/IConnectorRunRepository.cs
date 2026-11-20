using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IConnectorRunRepository
{
    Task AddAsync(ConnectorRun run, CancellationToken cancellationToken);
    Task UpdateAsync(ConnectorRun run, CancellationToken cancellationToken);
}
