using Cwi.Orchestrator.Application.Guardrails.Units;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Guardrails.Validators.Library;

public sealed class BoundsValidator : IGuardrailValidator
{
    public string Id => "quantity_bounds";

    public GuardrailValidationResult Validate(GuardrailContext context, GuardrailValidatorSettings settings)
    {
        if (context.Quantities.Count == 0)
        {
            return GuardrailValidationResult.Empty();
        }

        var unit = settings.GetString("unit");
        if (string.IsNullOrWhiteSpace(unit))
        {
            return GuardrailValidationResult.Empty();
        }

        var min = settings.GetDouble("min");
        var max = settings.GetDouble("max");
        if (min is null && max is null)
        {
            return GuardrailValidationResult.Empty();
        }

        if (!UnitNormalizer.TryParseQuantity($"0 {unit}", out _, out _))
        {
            return GuardrailValidationResult.FromViolations(new[]
            {
                new GuardrailViolation
                {
                    Id = Id,
                    Name = "Quantity bounds",
                    Severity = ResolveSeverity(settings),
                    Message = $"Unsupported bounds unit '{unit}'."
                }
            });
        }

        var minNormalized = min.HasValue
            ? UnitNormalizer.Normalize(new Quantity(min.Value, unit)).Normalized
            : null;
        var maxNormalized = max.HasValue
            ? UnitNormalizer.Normalize(new Quantity(max.Value, unit)).Normalized
            : null;

        var violations = new List<GuardrailViolation>();
        foreach (var quantity in context.Quantities)
        {
            if (!UnitNormalizer.AreCompatible(quantity, new Quantity(1, unit)))
            {
                continue;
            }

            var normalized = UnitNormalizer.Normalize(quantity);
            if (!normalized.Success || normalized.Normalized is null)
            {
                continue;
            }

            var value = normalized.Normalized.Value.Value;
            if (minNormalized.HasValue && value < minNormalized.Value.Value)
            {
                violations.Add(new GuardrailViolation
                {
                    Id = Id,
                    Name = "Quantity bounds",
                    Severity = ResolveSeverity(settings),
                    Message = $"Value {quantity.Value} {quantity.Unit} is below minimum {min} {unit}."
                });
            }

            if (maxNormalized.HasValue && value > maxNormalized.Value.Value)
            {
                violations.Add(new GuardrailViolation
                {
                    Id = Id,
                    Name = "Quantity bounds",
                    Severity = ResolveSeverity(settings),
                    Message = $"Value {quantity.Value} {quantity.Unit} exceeds maximum {max} {unit}."
                });
            }
        }

        return violations.Count == 0
            ? GuardrailValidationResult.Empty()
            : GuardrailValidationResult.FromViolations(violations);
    }

    private static string ResolveSeverity(GuardrailValidatorSettings settings)
        => settings.GetString("severity") ?? "medium";
}
