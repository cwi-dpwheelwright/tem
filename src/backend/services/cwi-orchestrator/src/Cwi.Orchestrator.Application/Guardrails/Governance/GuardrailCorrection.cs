namespace Cwi.Orchestrator.Application.Guardrails.Governance;

public sealed class GuardrailCorrection
{
    public bool Applied { get; init; }
    public string? Mode { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyDictionary<string, object?>? CorrectedPayload { get; init; }
    public IReadOnlyList<Models.CorrectionDiff> Corrections { get; init; } = Array.Empty<Models.CorrectionDiff>();
}
