namespace Cwi.Orchestrator.Api.Health;

public sealed class DependencyStatus
{
    public string Name { get; }
    public bool IsHealthy { get; }
    public string? Detail { get; }

    private DependencyStatus(string name, bool isHealthy, string? detail)
    {
        Name = name;
        IsHealthy = isHealthy;
        Detail = detail;
    }

    public static DependencyStatus Ok(string name)
    {
        return new DependencyStatus(name, true, null);
    }

    public static DependencyStatus Failed(string name, string detail)
    {
        return new DependencyStatus(name, false, detail);
    }
}
