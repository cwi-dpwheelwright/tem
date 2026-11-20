using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Common;
using Cwi.Orchestrator.Application.Guardrails;
using Cwi.Orchestrator.Application.Guardrails.Governance;
using Cwi.Orchestrator.Application.Guardrails.Scoring;
using Cwi.Orchestrator.Application.Guardrails.Validators;
using Cwi.Orchestrator.Application.Guardrails.Validators.Library;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Telemetry;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public class ActionGuardrailsStepTests
{
    [Fact]
    public async Task ExecuteAsync_sets_guardrail_decision_when_quantities_present()
    {
        var registry = new GuardrailValidatorRegistry(new IGuardrailValidator[]
        {
            new DimensionCompatibilityValidator(),
            new BoundsValidator()
        });
        var scorer = new SeverityScorer(new SeverityConfig());
        var evaluator = new GuardrailEvaluator(registry, scorer);
        var mapper = new GuardrailDecisionMapper(new GuardrailDecisionConfig());
        var correctionEngine = new GuardrailCorrectionEngine(new GuardrailCorrectionOptions());
        var metrics = new OrchestratorMetrics();
        var step = new ActionGuardrailsStep(evaluator, mapper, correctionEngine, metrics);

        var payload = new Dictionary<string, object?>
        {
            ["dose"] = "10 mg",
            ["length"] = "2 cm"
        };
        var context = new ActionWorkflowContext("tenant", "user", "observe", "set_dose", payload);

        var response = await step.ExecuteAsync(context, () =>
            Task.FromResult(ServiceResponse<ActionResponseDto>.Success(new ActionResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.GuardrailDecision.Should().NotBeNull();
        context.GuardrailViolations.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_applies_correction_for_bounds()
    {
        var registry = new GuardrailValidatorRegistry(new IGuardrailValidator[]
        {
            new DimensionCompatibilityValidator(),
            new BoundsValidator()
        });
        var scorer = new SeverityScorer(new SeverityConfig());
        var evaluator = new GuardrailEvaluator(registry, scorer);
        var mapper = new GuardrailDecisionMapper(new GuardrailDecisionConfig());
        var correctionEngine = new GuardrailCorrectionEngine(new GuardrailCorrectionOptions());
        var metrics = new OrchestratorMetrics();
        var step = new ActionGuardrailsStep(evaluator, mapper, correctionEngine, metrics);

        var payload = new Dictionary<string, object?>
        {
            ["dose"] = "5000 mg"
        };
        var context = new ActionWorkflowContext("tenant", "user", "observe", "set_dose", payload);

        var response = await step.ExecuteAsync(context, () =>
            Task.FromResult(ServiceResponse<ActionResponseDto>.Success(new ActionResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.GuardrailDecision.Should().NotBeNull();
        context.Payload["dose"].Should().NotBe("5000 mg");
    }
}
