namespace Cwi.Orchestrator.Application.Guardrails.Governance;

public sealed class GuardrailDecisionConfig
{
    public string AllowThreshold { get; init; } = "low";
    public string TicketThreshold { get; init; } = "medium";
}
