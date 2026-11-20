using Cwi.Orchestrator.Application.Guardrails.Scoring;
using Cwi.Orchestrator.Application.Models;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Guardrails;

public class SeverityScorerTests
{
    [Fact]
    public void Score_returns_none_when_no_violations()
    {
        var scorer = new SeverityScorer(new SeverityConfig());
        var result = scorer.Score(Array.Empty<GuardrailViolation>());

        result.Score.Should().Be(0.0);
        result.Classification.Should().Be("none");
    }

    [Fact]
    public void Score_uses_weights_and_thresholds()
    {
        var config = new SeverityConfig
        {
            Thresholds = new Dictionary<string, double>
            {
                ["low"] = 0.25,
                ["medium"] = 0.5,
                ["high"] = 0.75
            }
        };
        var scorer = new SeverityScorer(config);
        var violations = new[]
        {
            new GuardrailViolation { Severity = "high" },
            new GuardrailViolation { Severity = "high" }
        };

        var result = scorer.Score(violations);

        result.Score.Should().BeGreaterThan(0.6);
        result.Classification.Should().Be("high");
    }
}
