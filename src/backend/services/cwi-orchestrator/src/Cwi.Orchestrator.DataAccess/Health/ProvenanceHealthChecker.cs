using Cwi.Orchestrator.Domain.Health;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Health;

public sealed class ProvenanceHealthChecker : IProvenanceHealthChecker
{
    private readonly OrchestratorDbContext _db;

    public ProvenanceHealthChecker(OrchestratorDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.LedgerEvents
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedUtc)
                .Select(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Ledger unavailable: {ex.GetType().Name}");
        }
    }
}
