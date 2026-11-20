using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Guardrails.Validators;

public sealed class GuardrailValidationResult
{
    private GuardrailValidationResult(IReadOnlyList<GuardrailViolation> violations)
    {
        Violations = violations;
    }

    public IReadOnlyList<GuardrailViolation> Violations { get; }

    public static GuardrailValidationResult Empty() => new(Array.Empty<GuardrailViolation>());

    public static GuardrailValidationResult FromViolations(IReadOnlyList<GuardrailViolation> violations)
        => new(violations ?? Array.Empty<GuardrailViolation>());
}
