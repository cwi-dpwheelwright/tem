#nullable enable
using System.Text.Json;
using System.Collections.Generic;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Guardrails;
using Cwi.Orchestrator.Application.Guardrails.Config;
using Cwi.Orchestrator.Application.Guardrails.Governance;
using Cwi.Orchestrator.Application.Guardrails.Parsing;
using Cwi.Orchestrator.Application.Guardrails.Units;
using Cwi.Orchestrator.Application.Guardrails.Validators;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Telemetry;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class ActionGuardrailsStep : IActionWorkflowStep
{
    private readonly GuardrailEvaluator _evaluator;
    private readonly GuardrailDecisionMapper _decisionMapper;
    private readonly GuardrailCorrectionEngine _correctionEngine;
    private readonly OrchestratorMetrics _metrics;

    public ActionGuardrailsStep(
        GuardrailEvaluator evaluator,
        GuardrailDecisionMapper decisionMapper,
        GuardrailCorrectionEngine correctionEngine,
        OrchestratorMetrics metrics)
    {
        _evaluator = evaluator;
        _decisionMapper = decisionMapper;
        _correctionEngine = correctionEngine;
        _metrics = metrics;
    }

    public Task<ServiceResponse<ActionResponseDto>> ExecuteAsync(
        ActionWorkflowContext context,
        WorkflowStepDelegate<ActionResponseDto> next,
        CancellationToken cancellationToken)
    {
        var quantities = ExtractQuantities(context.Payload);
        if (quantities.Count == 0)
        {
            return next();
        }

        var guardrailContext = new GuardrailContext
        {
            Quantities = quantities
        };

        var config = new GuardrailConfig
        {
            Validators = new[]
            {
                new GuardrailValidatorConfig { Id = "dimension_compatibility" },
                new GuardrailValidatorConfig
                {
                    Id = "quantity_bounds",
                    Settings = new Dictionary<string, string>
                    {
                        ["unit"] = "mg",
                        ["max"] = "1000",
                        ["min"] = "0"
                    }
                }
            }
        };

        try
        {
            var evaluation = _evaluator.Evaluate(guardrailContext, config);
            if (context.GuardrailViolations.Count == 0)
            {
                context.GuardrailViolations = evaluation.Violations;
            }
            else
            {
                var combined = new List<GuardrailViolation>(context.GuardrailViolations.Count + evaluation.Violations.Count);
                combined.AddRange(context.GuardrailViolations);
                combined.AddRange(evaluation.Violations);
                context.GuardrailViolations = combined;
            }
            context.GuardrailSeverity = evaluation.Score.Classification;
            context.GuardrailDecision = _decisionMapper.Map(evaluation.Score.Classification);
            context.GuardrailDiagnostics = evaluation.Diagnostics;
            _metrics.RecordGuardrailViolations(evaluation.Violations);

            var correction = _correctionEngine.Apply(context.Payload, evaluation.Violations, evaluation.Score);
            context.CorrectionMode = correction.Mode;
            context.CorrectionSummary = correction.Summary;
            context.Corrections = correction.Corrections;
            _metrics.RecordGuardrailCorrection(correction.Mode);
            if (correction.Applied && correction.CorrectedPayload is not null)
            {
                context.Payload = new Dictionary<string, object?>(correction.CorrectedPayload, StringComparer.OrdinalIgnoreCase);
                context.GuardrailDecision = new GuardrailDecision
                {
                    Outcome = "corrected",
                    AllowExecution = true,
                    RequiresApproval = false,
                    Reason = correction.Summary ?? "Guardrails correction applied."
                };
            }
            else if (string.Equals(correction.Mode, "quarantine", StringComparison.OrdinalIgnoreCase))
            {
                context.GuardrailDecision = new GuardrailDecision
                {
                    Outcome = "quarantine",
                    AllowExecution = false,
                    RequiresApproval = true,
                    Reason = correction.Summary ?? "Guardrail violations quarantined."
                };
            }

            _metrics.RecordGuardrailOutcome(context.GuardrailDecision?.Outcome);
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(ServiceResponse<ActionResponseDto>.Fail(
                new DomainError(ErrorCodes.InternalError, ex.Message),
                statusCode: 500,
                message: "Guardrail evaluation failed."));
        }

        return next();
    }

    private static IReadOnlyList<Quantity> ExtractQuantities(IReadOnlyDictionary<string, object?> payload)
    {
        if (payload is null || payload.Count == 0)
        {
            return Array.Empty<Quantity>();
        }

        var results = new List<Quantity>();
        foreach (var value in payload.Values)
        {
            if (value is string text)
            {
                results.AddRange(QuantityExtractor.Extract(text));
                continue;
            }

            if (value is JsonElement element && element.ValueKind == JsonValueKind.String)
            {
                var extracted = element.GetString();
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    results.AddRange(QuantityExtractor.Extract(extracted));
                }
            }
        }

        return results;
    }
}
#nullable disable
