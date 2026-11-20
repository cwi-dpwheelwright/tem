namespace Cwi.Orchestrator.Application.Policy;

public sealed class KillSwitchAdminOptions
{
    public bool Enabled { get; init; } = true;
    public string? KillCode { get; init; }
    public bool TokenRequired { get; init; }
    public int TokenTtlSeconds { get; init; } = 300;
    public int TokenMaxTtlSeconds { get; init; } = 900;
}
