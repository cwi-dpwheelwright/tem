using Cwi.Orchestrator.Application.Guardrails.Units;

namespace Cwi.Orchestrator.Application.Guardrails.Validators;

public sealed class GuardrailContext
{
    public string? RawText { get; init; }
    public IReadOnlyList<Quantity> Quantities { get; init; } = Array.Empty<Quantity>();
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}
