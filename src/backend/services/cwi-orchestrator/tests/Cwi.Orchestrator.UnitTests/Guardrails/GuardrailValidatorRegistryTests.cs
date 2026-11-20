using Cwi.Orchestrator.Application.Guardrails.Validators;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Guardrails;

public class GuardrailValidatorRegistryTests
{
    [Fact]
    public void Registry_throws_on_duplicate_ids()
    {
        var validators = new IGuardrailValidator[]
        {
            new StubValidator("alpha"),
            new StubValidator("alpha")
        };

        var act = () => new GuardrailValidatorRegistry(validators);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Duplicate guardrail validator Id*");
    }

    [Fact]
    public void Registry_resolves_case_insensitive_ids()
    {
        var registry = new GuardrailValidatorRegistry(new IGuardrailValidator[] { new StubValidator("Physics") });

        registry.TryGet("physics", out var validator).Should().BeTrue();
        validator.Id.Should().Be("Physics");
    }

    [Fact]
    public void Resolve_throws_on_unknown_id()
    {
        var registry = new GuardrailValidatorRegistry(new IGuardrailValidator[] { new StubValidator("known") });
        var configs = new[] { new GuardrailValidatorConfig { Id = "missing" } };

        var act = () => registry.Resolve(configs);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown guardrail validator Id*");
    }

    [Fact]
    public void Settings_parses_numbers_in_invariant_culture()
    {
        var settings = new GuardrailValidatorSettings(new Dictionary<string, string>
        {
            ["threshold"] = "3.14",
            ["limit"] = "42"
        });

        settings.GetDouble("threshold").Should().Be(3.14);
        settings.GetInt("limit").Should().Be(42);
    }

    private sealed class StubValidator(string id) : IGuardrailValidator
    {
        public string Id { get; } = id;

        public GuardrailValidationResult Validate(GuardrailContext context, GuardrailValidatorSettings settings)
            => GuardrailValidationResult.Empty();
    }
}
