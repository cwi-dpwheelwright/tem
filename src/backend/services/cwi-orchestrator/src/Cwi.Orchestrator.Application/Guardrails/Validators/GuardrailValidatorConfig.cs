namespace Cwi.Orchestrator.Application.Guardrails.Validators;

public sealed class GuardrailValidatorConfig
{
    public string Id { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();
}
