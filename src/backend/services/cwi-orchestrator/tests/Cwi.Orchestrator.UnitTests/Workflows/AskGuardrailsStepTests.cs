using Cwi.Common;
using Cwi.Orchestrator.Application.Guardrails;
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

public class AskGuardrailsStepTests
{
    [Fact]
    public async Task ExecuteAsync_sets_guardrail_violations()
    {
        var registry = new GuardrailValidatorRegistry(new IGuardrailValidator[]
        {
            new DimensionCompatibilityValidator(),
            new BoundsValidator()
        });
        var scorer = new SeverityScorer(new SeverityConfig());
        var evaluator = new GuardrailEvaluator(registry, scorer);
        var metrics = new OrchestratorMetrics();
        var step = new AskGuardrailsStep(evaluator, metrics);

        var context = new AskWorkflowContext("tenant", "user", "question", "pack", null, "observe", 3)
        {
            Answer = "Length 1 m and mass 1 kg"
        };

        var response = await step.ExecuteAsync(context, () =>
            Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.GuardrailViolations.Should().NotBeEmpty();
    }
}
