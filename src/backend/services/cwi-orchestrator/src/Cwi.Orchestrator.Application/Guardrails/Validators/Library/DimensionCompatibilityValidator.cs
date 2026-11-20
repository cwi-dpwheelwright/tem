using Cwi.Orchestrator.Application.Guardrails.Units;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Guardrails.Validators.Library;

public sealed class DimensionCompatibilityValidator : IGuardrailValidator
{
    public string Id => "dimension_compatibility";

    public GuardrailValidationResult Validate(GuardrailContext context, GuardrailValidatorSettings settings)
    {
        if (context.Quantities.Count < 2)
        {
            return GuardrailValidationResult.Empty();
        }

        var dimensions = new HashSet<Dimension>();
        foreach (var quantity in context.Quantities)
        {
            var normalized = UnitNormalizer.Normalize(quantity);
            if (!normalized.Success || normalized.Normalized is null)
            {
                return GuardrailValidationResult.FromViolations(new[]
                {
                    new GuardrailViolation
                    {
                        Id = Id,
                        Name = "Dimension compatibility",
                        Severity = ResolveSeverity(settings),
                        Message = normalized.Error ?? $"Unsupported unit '{quantity.Unit}'."
                    }
                });
            }

            dimensions.Add(normalized.Normalized.Value.Dimension);
        }

        if (dimensions.Count <= 1)
        {
            return GuardrailValidationResult.Empty();
        }

        return GuardrailValidationResult.FromViolations(new[]
        {
            new GuardrailViolation
            {
                Id = Id,
                Name = "Dimension compatibility",
                Severity = ResolveSeverity(settings),
                Message = "Quantities span multiple dimensions."
            }
        });
    }

    private static string ResolveSeverity(GuardrailValidatorSettings settings)
        => settings.GetString("severity") ?? "high";
}
