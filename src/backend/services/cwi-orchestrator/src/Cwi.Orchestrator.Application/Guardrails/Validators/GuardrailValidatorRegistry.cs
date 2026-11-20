namespace Cwi.Orchestrator.Application.Guardrails.Validators;

public sealed class GuardrailValidatorRegistry
{
    private readonly Dictionary<string, IGuardrailValidator> _validators;

    public GuardrailValidatorRegistry(IEnumerable<IGuardrailValidator> validators)
    {
        _validators = new Dictionary<string, IGuardrailValidator>(StringComparer.OrdinalIgnoreCase);
        foreach (var validator in validators ?? Array.Empty<IGuardrailValidator>())
        {
            if (string.IsNullOrWhiteSpace(validator.Id))
            {
                throw new InvalidOperationException("Guardrail validator must define a non-empty Id.");
            }

            if (!_validators.TryAdd(validator.Id, validator))
            {
                throw new InvalidOperationException($"Duplicate guardrail validator Id '{validator.Id}'.");
            }
        }
    }

    public IReadOnlyCollection<IGuardrailValidator> All => _validators.Values.ToArray();

    public bool TryGet(string id, out IGuardrailValidator validator)
        => _validators.TryGetValue(id, out validator!);

    public IReadOnlyList<IGuardrailValidator> Resolve(IEnumerable<GuardrailValidatorConfig> configs)
    {
        if (configs == null)
        {
            return Array.Empty<IGuardrailValidator>();
        }

        var resolved = new List<IGuardrailValidator>();
        foreach (var config in configs)
        {
            if (!_validators.TryGetValue(config.Id, out var validator))
            {
                throw new InvalidOperationException($"Unknown guardrail validator Id '{config.Id}'.");
            }

            resolved.Add(validator);
        }

        return resolved;
    }
}
