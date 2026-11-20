namespace Cwi.Orchestrator.Application.BreakGlass;

public sealed class BreakGlassPolicy
{
    public IReadOnlyList<string> AllowedRoles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AllowedScopes { get; init; } = Array.Empty<string>();
    public int MaxDurationMinutes { get; init; } = 60;
    public bool RequireReason { get; init; } = true;
}
