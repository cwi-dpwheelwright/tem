using Cwi.Orchestrator.Application.Guardrails;
using Cwi.Orchestrator.Application.Guardrails.Config;
using Cwi.Orchestrator.Application.Guardrails.Scoring;
using Cwi.Orchestrator.Application.Guardrails.Validators;
using Cwi.Orchestrator.Application.Guardrails.Validators.Library;
using Cwi.Orchestrator.Application.Guardrails.Units;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Guardrails;

public class GuardrailEvaluatorTests
{
    [Fact]
    public void Evaluate_returns_diagnostics_and_score()
    {
        var registry = new GuardrailValidatorRegistry(new IGuardrailValidator[]
        {
            new BoundsValidator()
        });
        var scorer = new SeverityScorer(new SeverityConfig());
        var evaluator = new GuardrailEvaluator(registry, scorer);
        var config = new GuardrailConfig
        {
            Validators = new[]
            {
                new GuardrailValidatorConfig
                {
                    Id = "quantity_bounds",
                    Settings = new Dictionary<string, string>
                    {
                        ["unit"] = "mg",
                        ["max"] = "5",
                        ["severity"] = "high"
                    }
                }
            }
        };

        var context = new GuardrailContext
        {
            Quantities = new[] { new Quantity(10, "mg") }
        };

        var result = evaluator.Evaluate(context, config);

        result.Violations.Should().HaveCount(1);
        result.Diagnostics.TotalViolations.Should().Be(1);
        result.Diagnostics.SeverityCounts.Should().ContainKey("high");
        result.Diagnostics.ValidatorCounts.Should().ContainKey("quantity_bounds");
        result.Score.Classification.Should().NotBe("none");
    }
}
