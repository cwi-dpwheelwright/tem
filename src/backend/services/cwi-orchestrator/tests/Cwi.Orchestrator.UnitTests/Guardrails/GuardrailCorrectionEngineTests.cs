using System.Collections.Generic;
using Cwi.Orchestrator.Application.Guardrails.Governance;
using Cwi.Orchestrator.Application.Guardrails.Scoring;
using Cwi.Orchestrator.Application.Models;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Guardrails;

public sealed class GuardrailCorrectionEngineTests
{
    [Fact]
    public void Apply_AdjustsQuantities_WhenSeverityLow()
    {
        var engine = new GuardrailCorrectionEngine(new GuardrailCorrectionOptions());
        var payload = new Dictionary<string, object?> { { "dose", "5000 mg" } };
        var violations = new List<GuardrailViolation>
        {
            new GuardrailViolation { Id = "quantity_bounds", Severity = "medium", Message = "too high" }
        };

        var correction = engine.Apply(payload, violations, new SeverityScore(0.4, "medium"));

        Assert.True(correction.Applied);
        Assert.NotNull(correction.CorrectedPayload);
        Assert.NotEqual("5000 mg", correction.CorrectedPayload!["dose"]);
    }

    [Fact]
    public void Apply_Skips_WhenSeverityHigh()
    {
        var engine = new GuardrailCorrectionEngine(new GuardrailCorrectionOptions());
        var payload = new Dictionary<string, object?> { { "dose", "5000 mg" } };
        var violations = new List<GuardrailViolation>
        {
            new GuardrailViolation { Id = "quantity_bounds", Severity = "high", Message = "too high" }
        };

        var correction = engine.Apply(payload, violations, new SeverityScore(0.9, "high"));

        Assert.False(correction.Applied);
        Assert.Null(correction.CorrectedPayload);
    }
}
