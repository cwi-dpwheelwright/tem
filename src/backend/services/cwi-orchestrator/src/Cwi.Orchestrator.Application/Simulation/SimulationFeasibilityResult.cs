#nullable enable
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Simulation;

public sealed class SimulationFeasibilityResult
{
    public bool IsFeasible { get; init; }

    public string Severity { get; init; } = "low";

    public string? Reason { get; init; }

    public bool TimedOut { get; init; }

    public IReadOnlyList<GuardrailViolation> Violations { get; init; } = Array.Empty<GuardrailViolation>();
}
#nullable disable
