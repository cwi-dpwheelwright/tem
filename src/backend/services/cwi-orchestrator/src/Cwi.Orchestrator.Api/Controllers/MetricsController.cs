using System.Text;
using Cwi.Orchestrator.Api.Health;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/metrics")]
public sealed class MetricsController : ControllerBase
{
    private readonly DependencyHealthChecker _checker;
    private readonly Cwi.Orchestrator.Application.Telemetry.OrchestratorMetrics _metrics;

    public MetricsController(
        DependencyHealthChecker checker,
        Cwi.Orchestrator.Application.Telemetry.OrchestratorMetrics metrics)
    {
        _checker = checker;
        _metrics = metrics;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var report = await _checker.CheckAsync(cancellationToken).ConfigureAwait(false);

        var builder = new StringBuilder();
        builder.AppendLine("# HELP cwi_orchestrator_ready Overall readiness based on dependency checks.");
        builder.AppendLine("# TYPE cwi_orchestrator_ready gauge");
        builder.AppendLine($"cwi_orchestrator_ready {(report.IsHealthy ? 1 : 0)}");

        builder.AppendLine("# HELP cwi_orchestrator_dependency_up Dependency health status (1=up, 0=down).");
        builder.AppendLine("# TYPE cwi_orchestrator_dependency_up gauge");
        foreach (var dependency in report.Dependencies)
        {
            builder.Append("cwi_orchestrator_dependency_up{dependency=\"");
            builder.Append(dependency.Name);
            builder.Append("\"} ");
            builder.AppendLine(dependency.IsHealthy ? "1" : "0");
        }

        var snapshot = _metrics.Snapshot();
        builder.AppendLine("# HELP cwi_orchestrator_discrepancy_total Total discrepancies detected by severity.");
        builder.AppendLine("# TYPE cwi_orchestrator_discrepancy_total counter");
        foreach (var entry in snapshot.DiscrepanciesBySeverity)
        {
            builder.Append("cwi_orchestrator_discrepancy_total{severity=\"");
            builder.Append(entry.Key);
            builder.Append("\"} ");
            builder.AppendLine(entry.Value.ToString());
        }

        builder.AppendLine("# HELP cwi_orchestrator_guardrail_violation_total Total guardrail violations by severity.");
        builder.AppendLine("# TYPE cwi_orchestrator_guardrail_violation_total counter");
        foreach (var entry in snapshot.GuardrailViolationsBySeverity)
        {
            builder.Append("cwi_orchestrator_guardrail_violation_total{severity=\"");
            builder.Append(entry.Key);
            builder.Append("\"} ");
            builder.AppendLine(entry.Value.ToString());
        }

        builder.AppendLine("# HELP cwi_orchestrator_guardrail_outcome_total Guardrail decision outcomes.");
        builder.AppendLine("# TYPE cwi_orchestrator_guardrail_outcome_total counter");
        foreach (var entry in snapshot.GuardrailOutcomes)
        {
            builder.Append("cwi_orchestrator_guardrail_outcome_total{outcome=\"");
            builder.Append(entry.Key);
            builder.Append("\"} ");
            builder.AppendLine(entry.Value.ToString());
        }

        builder.AppendLine("# HELP cwi_orchestrator_guardrail_correction_total Guardrail correction modes.");
        builder.AppendLine("# TYPE cwi_orchestrator_guardrail_correction_total counter");
        foreach (var entry in snapshot.GuardrailCorrections)
        {
            builder.Append("cwi_orchestrator_guardrail_correction_total{mode=\"");
            builder.Append(entry.Key);
            builder.Append("\"} ");
            builder.AppendLine(entry.Value.ToString());
        }

        builder.AppendLine("# HELP cwi_orchestrator_ownership_transfer_requested_total Ownership transfer/delegation requests by type.");
        builder.AppendLine("# TYPE cwi_orchestrator_ownership_transfer_requested_total counter");
        foreach (var entry in snapshot.OwnershipTransfersRequested)
        {
            builder.Append("cwi_orchestrator_ownership_transfer_requested_total{type=\"");
            builder.Append(entry.Key);
            builder.Append("\"} ");
            builder.AppendLine(entry.Value.ToString());
        }

        builder.AppendLine("# HELP cwi_orchestrator_ownership_transfer_approved_total Ownership transfer/delegation approvals by type.");
        builder.AppendLine("# TYPE cwi_orchestrator_ownership_transfer_approved_total counter");
        foreach (var entry in snapshot.OwnershipTransfersApproved)
        {
            builder.Append("cwi_orchestrator_ownership_transfer_approved_total{type=\"");
            builder.Append(entry.Key);
            builder.Append("\"} ");
            builder.AppendLine(entry.Value.ToString());
        }

        builder.AppendLine("# HELP cwi_orchestrator_ownership_transfer_revoked_total Ownership transfer/delegation revocations by type.");
        builder.AppendLine("# TYPE cwi_orchestrator_ownership_transfer_revoked_total counter");
        foreach (var entry in snapshot.OwnershipTransfersRevoked)
        {
            builder.Append("cwi_orchestrator_ownership_transfer_revoked_total{type=\"");
            builder.Append(entry.Key);
            builder.Append("\"} ");
            builder.AppendLine(entry.Value.ToString());
        }

        builder.AppendLine("# HELP cwi_orchestrator_operator_profile_evaluations_total Operator profile policy evaluations by rule.");
        builder.AppendLine("# TYPE cwi_orchestrator_operator_profile_evaluations_total counter");
        foreach (var entry in snapshot.OperatorProfileEvaluationsByRule)
        {
            builder.Append("cwi_orchestrator_operator_profile_evaluations_total{rule=\"");
            builder.Append(entry.Key);
            builder.Append("\"} ");
            builder.AppendLine(entry.Value.ToString());
        }

        builder.AppendLine("# HELP cwi_orchestrator_operator_profile_adjustments_total Operator profile policy adjustments applied by rule.");
        builder.AppendLine("# TYPE cwi_orchestrator_operator_profile_adjustments_total counter");
        foreach (var entry in snapshot.OperatorProfileAdjustmentsByRule)
        {
            builder.Append("cwi_orchestrator_operator_profile_adjustments_total{rule=\"");
            builder.Append(entry.Key);
            builder.Append("\"} ");
            builder.AppendLine(entry.Value.ToString());
        }

        builder.AppendLine("# HELP cwi_requests_total Total ASK requests by aggregate tenant/node/status labels.");
        builder.AppendLine("# TYPE cwi_requests_total counter");
        foreach (var entry in snapshot.Sustainability)
        {
            AppendSustainabilityMetric(builder, "cwi_requests_total", entry, entry.RequestsTotal);
        }

        builder.AppendLine("# HELP cwi_request_energy_wh_total Total request energy in watt-hours by aggregate tenant/node/status labels.");
        builder.AppendLine("# TYPE cwi_request_energy_wh_total counter");
        foreach (var entry in snapshot.Sustainability)
        {
            AppendSustainabilityMetric(builder, "cwi_request_energy_wh_total", entry, entry.EnergyWhTotal);
        }

        builder.AppendLine("# HELP cwi_request_cost_usd_total Total request cost in USD by aggregate tenant/node/status labels.");
        builder.AppendLine("# TYPE cwi_request_cost_usd_total counter");
        foreach (var entry in snapshot.Sustainability)
        {
            AppendSustainabilityMetric(builder, "cwi_request_cost_usd_total", entry, entry.CostUsdTotal);
        }

        builder.AppendLine("# HELP cwi_request_water_liters_total Total estimated request water in liters by aggregate tenant/node/status labels.");
        builder.AppendLine("# TYPE cwi_request_water_liters_total counter");
        foreach (var entry in snapshot.Sustainability)
        {
            AppendSustainabilityMetric(builder, "cwi_request_water_liters_total", entry, entry.WaterLitersTotal);
        }

        builder.AppendLine("# HELP cwi_cloud_baseline_energy_wh_total Total versioned cloud-baseline request energy in watt-hours by aggregate tenant/node/status labels.");
        builder.AppendLine("# TYPE cwi_cloud_baseline_energy_wh_total counter");
        foreach (var entry in snapshot.Sustainability)
        {
            AppendSustainabilityMetric(builder, "cwi_cloud_baseline_energy_wh_total", entry, entry.CloudBaselineEnergyWhTotal);
        }

        builder.AppendLine("# HELP cwi_cloud_baseline_cost_usd_total Total versioned cloud-baseline request cost in USD by aggregate tenant/node/status labels.");
        builder.AppendLine("# TYPE cwi_cloud_baseline_cost_usd_total counter");
        foreach (var entry in snapshot.Sustainability)
        {
            AppendSustainabilityMetric(builder, "cwi_cloud_baseline_cost_usd_total", entry, entry.CloudBaselineCostUsdTotal);
        }

        builder.AppendLine("# HELP cwi_cloud_baseline_water_liters_total Total versioned cloud-baseline request water in liters by aggregate tenant/node/status labels.");
        builder.AppendLine("# TYPE cwi_cloud_baseline_water_liters_total counter");
        foreach (var entry in snapshot.Sustainability)
        {
            AppendSustainabilityMetric(builder, "cwi_cloud_baseline_water_liters_total", entry, entry.CloudBaselineWaterLitersTotal);
        }

        builder.AppendLine("# HELP cwi_energy_saved_wh_total Total estimated request energy savings in watt-hours by aggregate tenant/node/status labels.");
        builder.AppendLine("# TYPE cwi_energy_saved_wh_total counter");
        foreach (var entry in snapshot.Sustainability)
        {
            AppendSustainabilityMetric(builder, "cwi_energy_saved_wh_total", entry, entry.EnergySavedWhTotal);
        }

        builder.AppendLine("# HELP cwi_cost_saved_usd_total Total estimated request cost savings in USD by aggregate tenant/node/status labels.");
        builder.AppendLine("# TYPE cwi_cost_saved_usd_total counter");
        foreach (var entry in snapshot.Sustainability)
        {
            AppendSustainabilityMetric(builder, "cwi_cost_saved_usd_total", entry, entry.CostSavedUsdTotal);
        }

        builder.AppendLine("# HELP cwi_water_saved_liters_total Total estimated request water savings in liters by aggregate tenant/node/status labels.");
        builder.AppendLine("# TYPE cwi_water_saved_liters_total counter");
        foreach (var entry in snapshot.Sustainability)
        {
            AppendSustainabilityMetric(builder, "cwi_water_saved_liters_total", entry, entry.WaterSavedLitersTotal);
        }

        builder.AppendLine("# HELP cwi_request_duration_seconds_sum Sum of ASK request durations by aggregate tenant/node/status labels.");
        builder.AppendLine("# TYPE cwi_request_duration_seconds_sum counter");
        foreach (var entry in snapshot.Sustainability)
        {
            AppendSustainabilityMetric(builder, "cwi_request_duration_seconds_sum", entry, entry.DurationSecondsSum);
        }

        builder.AppendLine("# HELP cwi_request_duration_seconds_count Count of ASK request durations by aggregate tenant/node/status labels.");
        builder.AppendLine("# TYPE cwi_request_duration_seconds_count counter");
        foreach (var entry in snapshot.Sustainability)
        {
            AppendSustainabilityMetric(builder, "cwi_request_duration_seconds_count", entry, entry.DurationSecondsCount);
        }

        builder.AppendLine("# HELP cwi_orchestrator_simulation_infeasible_total Simulation infeasible evaluations.");
        builder.AppendLine("# TYPE cwi_orchestrator_simulation_infeasible_total counter");
        builder.AppendLine($"cwi_orchestrator_simulation_infeasible_total {snapshot.SimulationInfeasibleTotal}");

        builder.AppendLine("# HELP cwi_orchestrator_simulation_timeout_total Simulation feasibility timeouts.");
        builder.AppendLine("# TYPE cwi_orchestrator_simulation_timeout_total counter");
        builder.AppendLine($"cwi_orchestrator_simulation_timeout_total {snapshot.SimulationTimeoutTotal}");

        return Content(builder.ToString(), "text/plain; version=0.0.4");
    }

    private static void AppendSustainabilityMetric(
        StringBuilder builder,
        string metricName,
        Cwi.Orchestrator.Application.Models.SustainabilityAggregateMetricDto entry,
        double value)
    {
        builder.Append(metricName);
        builder.Append("{tenant_id=\"");
        builder.Append(EscapeLabel(entry.TenantId));
        builder.Append("\",node_id=\"");
        builder.Append(EscapeLabel(entry.NodeId));
        builder.Append("\",node_name=\"");
        builder.Append(EscapeLabel(entry.NodeName));
        builder.Append("\",node_group=\"");
        builder.Append(EscapeLabel(entry.NodeGroup));
        builder.Append("\",node_role=\"");
        builder.Append(EscapeLabel(entry.NodeRole));
        builder.Append("\",status=\"");
        builder.Append(EscapeLabel(entry.Status));
        builder.Append("\"} ");
        builder.AppendLine(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string EscapeLabel(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
