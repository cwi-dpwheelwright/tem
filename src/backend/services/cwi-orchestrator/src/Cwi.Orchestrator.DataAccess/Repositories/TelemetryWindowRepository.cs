using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class TelemetryWindowRepository : ITelemetryWindowRepository
{
    private readonly OrchestratorDbContext _db;

    public TelemetryWindowRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(TelemetryWindow window, CancellationToken cancellationToken)
    {
        _db.TelemetryWindows.Add(window);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
