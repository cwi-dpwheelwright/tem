namespace Cwi.Orchestrator.Application.Guardrails.Diagnostics;

public sealed class GuardrailDiagnostics
{
    public int TotalViolations { get; init; }
    public IReadOnlyDictionary<string, int> SeverityCounts { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> ValidatorCounts { get; init; } = new Dictionary<string, int>();
}
