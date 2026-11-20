namespace Cwi.Orchestrator.Domain.Health;

public sealed class HealthCheckResult
{
    public bool IsHealthy { get; }
    public string? Detail { get; }

    public HealthCheckResult(bool isHealthy, string? detail = null)
    {
        IsHealthy = isHealthy;
        Detail = detail;
    }

    public static HealthCheckResult Healthy()
    {
        return new HealthCheckResult(true);
    }

    public static HealthCheckResult Unhealthy(string detail)
    {
        return new HealthCheckResult(false, detail);
    }
}
