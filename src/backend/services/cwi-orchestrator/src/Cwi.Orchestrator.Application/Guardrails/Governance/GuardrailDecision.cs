namespace Cwi.Orchestrator.Application.Guardrails.Governance;

public sealed class GuardrailDecision
{
    public string Outcome { get; init; } = "allow";
    public bool RequiresApproval { get; init; }
    public bool AllowExecution { get; init; } = true;
    public string Reason { get; init; } = string.Empty;
}
