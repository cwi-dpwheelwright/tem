namespace Cwi.Orchestrator.Api.Health;

public sealed class DependencyHealthReport
{
    public DependencyHealthReport(params DependencyStatus[] dependencies)
    {
        Dependencies = dependencies;
    }

    public IReadOnlyList<DependencyStatus> Dependencies { get; }

    public bool IsHealthy => Dependencies.All(dep => dep.IsHealthy);
}
