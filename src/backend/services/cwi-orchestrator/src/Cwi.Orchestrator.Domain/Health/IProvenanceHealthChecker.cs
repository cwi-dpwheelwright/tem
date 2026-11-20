namespace Cwi.Orchestrator.Domain.Health;

public interface IProvenanceHealthChecker
{
    Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken);
}
