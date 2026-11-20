using Cwi.Orchestrator.Application.Guardrails.Validators;

namespace Cwi.Orchestrator.Application.Guardrails.Config;

public sealed class GuardrailConfig
{
    public int Version { get; init; } = 1;
    public IReadOnlyList<GuardrailValidatorConfig> Validators { get; init; } = Array.Empty<GuardrailValidatorConfig>();
}
