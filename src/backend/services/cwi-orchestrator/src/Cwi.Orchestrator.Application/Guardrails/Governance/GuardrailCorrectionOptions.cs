namespace Cwi.Orchestrator.Application.Guardrails.Governance;

public sealed class GuardrailCorrectionOptions
{
    public bool Enabled { get; init; } = true;
    public string Mode { get; init; } = "auto_correct";
    public string MaxSeverity { get; init; } = "medium";
}
