using Cwi.Orchestrator.Application.Guardrails.Units;
using Cwi.Orchestrator.Application.Guardrails.Validators;
using Cwi.Orchestrator.Application.Guardrails.Validators.Library;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Guardrails;

public class BaselineValidatorsTests
{
    [Fact]
    public void BoundsValidator_flags_out_of_range()
    {
        var validator = new BoundsValidator();
        var context = new GuardrailContext
        {
            Quantities = new[] { new Quantity(10, "mg") }
        };
        var settings = new GuardrailValidatorSettings(new Dictionary<string, string>
        {
            ["unit"] = "mg",
            ["max"] = "5",
            ["severity"] = "high"
        });

        var result = validator.Validate(context, settings);

        result.Violations.Should().ContainSingle();
        result.Violations[0].Severity.Should().Be("high");
    }

    [Fact]
    public void DimensionCompatibilityValidator_flags_mixed_dimensions()
    {
        var validator = new DimensionCompatibilityValidator();
        var context = new GuardrailContext
        {
            Quantities = new[] { new Quantity(1, "m"), new Quantity(1, "kg") }
        };

        var result = validator.Validate(context, new GuardrailValidatorSettings());

        result.Violations.Should().ContainSingle();
        result.Violations[0].Id.Should().Be("dimension_compatibility");
    }
}
