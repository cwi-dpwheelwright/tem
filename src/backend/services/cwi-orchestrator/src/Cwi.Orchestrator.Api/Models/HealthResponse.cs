namespace Cwi.Orchestrator.Api.Models;

public sealed class HealthResponse
{
    public bool Ok { get; init; }
    public Dictionary<string, string> Services { get; init; } = new();
}
