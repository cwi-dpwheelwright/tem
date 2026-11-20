namespace Cwi.Orchestrator.Application.Guardrails.Validators;

public interface IGuardrailValidator
{
    string Id { get; }
    GuardrailValidationResult Validate(GuardrailContext context, GuardrailValidatorSettings settings);
}
