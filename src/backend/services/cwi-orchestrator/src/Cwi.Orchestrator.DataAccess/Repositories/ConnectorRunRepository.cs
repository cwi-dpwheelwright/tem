using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class ConnectorRunRepository : IConnectorRunRepository
{
    private readonly OrchestratorDbContext _db;

    public ConnectorRunRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(ConnectorRun run, CancellationToken cancellationToken)
    {
        _db.ConnectorRuns.Add(run);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(ConnectorRun run, CancellationToken cancellationToken)
    {
        _db.ConnectorRuns.Update(run);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
