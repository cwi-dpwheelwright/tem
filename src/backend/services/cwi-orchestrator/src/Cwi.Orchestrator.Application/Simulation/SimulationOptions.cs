namespace Cwi.Orchestrator.Application.Simulation;

public sealed class SimulationOptions
{
    public bool Enabled { get; init; } = true;
    public int TimeoutMs { get; init; } = 250;

    public TimeSpan TimeBudget
        => TimeoutMs <= 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(TimeoutMs);
}
