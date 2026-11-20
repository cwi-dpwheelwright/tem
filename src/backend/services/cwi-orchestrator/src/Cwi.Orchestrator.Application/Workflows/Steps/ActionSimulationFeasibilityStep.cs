#nullable enable
using Cwi.Common;
using Cwi.Orchestrator.Application.Guardrails.Governance;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Simulation;
using Cwi.Orchestrator.Application.Telemetry;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class ActionSimulationFeasibilityStep : IActionWorkflowStep
{
    private readonly ISimulationFeasibilityEvaluator _evaluator;
    private readonly SimulationOptions _options;
    private readonly OrchestratorMetrics _metrics;

    public ActionSimulationFeasibilityStep(
        ISimulationFeasibilityEvaluator evaluator,
        SimulationOptions options,
        OrchestratorMetrics metrics)
    {
        _evaluator = evaluator;
        _options = options;
        _metrics = metrics;
    }

    public async Task<ServiceResponse<ActionResponseDto>> ExecuteAsync(
        ActionWorkflowContext context,
        WorkflowStepDelegate<ActionResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return await next().ConfigureAwait(false);
        }

        var request = new SimulationFeasibilityRequest(
            context.ActionType,
            context.Payload,
            _options.TimeBudget);

        var result = await _evaluator.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        _metrics.RecordSimulationResult(result);
        if (!result.IsFeasible)
        {
            context.SimulationFeasibility = result;
            if (result.Violations.Count > 0)
            {
                var combined = new List<GuardrailViolation>(context.GuardrailViolations.Count + result.Violations.Count);
                combined.AddRange(context.GuardrailViolations);
                combined.AddRange(result.Violations);
                context.GuardrailViolations = combined;
            }

            context.SimulationDecision = new GuardrailDecision
            {
                Outcome = result.TimedOut ? "simulation_timeout" : "simulation_infeasible",
                AllowExecution = false,
                RequiresApproval = true,
                Reason = result.Reason ?? "Simulation feasibility check failed."
            };
        }

        return await next().ConfigureAwait(false);
    }
}
#nullable disable
