namespace Cwi.Router.Application.Health;

public sealed class DependencyHealthReport
{
    public DependencyStatus Orchestrator { get; }
    public DependencyStatus Redis { get; }

    public DependencyHealthReport(DependencyStatus orchestrator, DependencyStatus redis)
    {
        Orchestrator = orchestrator;
        Redis = redis;
    }

    public bool IsHealthy => Orchestrator.IsHealthy && Redis.IsHealthy;
}
