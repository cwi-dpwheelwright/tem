namespace Cwi.Orchestrator.Application.Guardrails.Governance;

public sealed class GuardrailDecisionMapper
{
    private readonly GuardrailDecisionConfig _config;

    public GuardrailDecisionMapper(GuardrailDecisionConfig config)
    {
        _config = config ?? new GuardrailDecisionConfig();
    }

    public GuardrailDecision Map(string? classification)
    {
        var normalized = (classification ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalized) || normalized == "none")
        {
            return Allow("No guardrail violations.");
        }

        if (normalized == _config.AllowThreshold)
        {
            return Allow($"Guardrail severity '{normalized}' within allow threshold.");
        }

        if (normalized == _config.TicketThreshold)
        {
            return Ticket($"Guardrail severity '{normalized}' requires approval.");
        }

        return Block($"Guardrail severity '{normalized}' exceeds approval threshold.");
    }

    private static GuardrailDecision Allow(string reason)
        => new()
        {
            Outcome = "allow",
            AllowExecution = true,
            RequiresApproval = false,
            Reason = reason
        };

    private static GuardrailDecision Ticket(string reason)
        => new()
        {
            Outcome = "ticket",
            AllowExecution = false,
            RequiresApproval = true,
            Reason = reason
        };

    private static GuardrailDecision Block(string reason)
        => new()
        {
            Outcome = "block",
            AllowExecution = false,
            RequiresApproval = false,
            Reason = reason
        };
}
