#nullable enable
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Guardrails;
using Cwi.Orchestrator.Application.Guardrails.Config;
using Cwi.Orchestrator.Application.Guardrails.Parsing;
using Cwi.Orchestrator.Application.Guardrails.Validators;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Telemetry;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class AskGuardrailsStep : IAskWorkflowStep
{
    private readonly GuardrailEvaluator _evaluator;
    private readonly OrchestratorMetrics _metrics;

    public AskGuardrailsStep(GuardrailEvaluator evaluator, OrchestratorMetrics metrics)
    {
        _evaluator = evaluator;
        _metrics = metrics;
    }

    public Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Answer))
        {
            return next();
        }

        var quantities = QuantityExtractor.Extract(context.Answer);
        var guardrailContext = new GuardrailContext
        {
            RawText = context.Answer,
            Quantities = quantities
        };

        var config = new GuardrailConfig
        {
            Validators = new[]
            {
                new GuardrailValidatorConfig { Id = "dimension_compatibility" },
                new GuardrailValidatorConfig { Id = "quantity_bounds" }
            }
        };

        try
        {
            var evaluation = _evaluator.Evaluate(guardrailContext, config);
            context.GuardrailViolations = evaluation.Violations;
            context.GuardrailDiagnostics = evaluation.Diagnostics;
            _metrics.RecordGuardrailViolations(evaluation.Violations);
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(ServiceResponse<AskResponseDto>.Fail(
                new DomainError(ErrorCodes.InternalError, ex.Message),
                statusCode: 500,
                message: "Guardrail evaluation failed."));
        }

        return next();
    }
}
#nullable disable
