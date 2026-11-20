namespace Cwi.Orchestrator.Application.BreakGlass;

public sealed class BreakGlassRequest
{
    public string Role { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public int DurationMinutes { get; init; }
    public string? Reason { get; init; }
}
