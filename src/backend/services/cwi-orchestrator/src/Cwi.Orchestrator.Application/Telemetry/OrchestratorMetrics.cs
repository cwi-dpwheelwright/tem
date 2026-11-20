using System.Collections.Concurrent;
using System.Threading;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Simulation;
using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Application.Telemetry;

public sealed class OrchestratorMetrics
{
    private readonly ConcurrentDictionary<string, long> _discrepanciesBySeverity = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _guardrailViolationsBySeverity = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _guardrailOutcomes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _guardrailCorrections = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _ownershipTransfersRequested = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _ownershipTransfersApproved = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _ownershipTransfersRevoked = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _operatorProfileEvaluationsByRule = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _operatorProfileAdjustmentsByRule = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<SustainabilityMetricKey, SustainabilityMetricAccumulator> _sustainability = new();
    private long _simulationInfeasibleTotal;
    private long _simulationTimeoutTotal;

    public void RecordDiscrepancy(string? severity)
    {
        var key = Normalize(severity);
        Increment(_discrepanciesBySeverity, key);
    }

    public void RecordGuardrailViolations(IReadOnlyList<GuardrailViolation> violations)
    {
        if (violations is null || violations.Count == 0)
        {
            return;
        }

        foreach (var violation in violations)
        {
            Increment(_guardrailViolationsBySeverity, Normalize(violation.Severity));
        }
    }

    public void RecordGuardrailOutcome(string? outcome)
    {
        Increment(_guardrailOutcomes, Normalize(outcome));
    }

    public void RecordGuardrailCorrection(string? mode)
    {
        Increment(_guardrailCorrections, Normalize(mode));
    }

    public void RecordSimulationResult(SimulationFeasibilityResult result)
    {
        if (!result.IsFeasible)
        {
            Interlocked.Increment(ref _simulationInfeasibleTotal);
        }

        if (result.TimedOut)
        {
            Interlocked.Increment(ref _simulationTimeoutTotal);
        }
    }

    public void RecordOwnershipTransferRequested(string? transferType)
    {
        Increment(_ownershipTransfersRequested, Normalize(transferType));
    }

    public void RecordOwnershipTransferApproved(string? transferType)
    {
        Increment(_ownershipTransfersApproved, Normalize(transferType));
    }

    public void RecordOwnershipTransferRevoked(string? transferType)
    {
        Increment(_ownershipTransfersRevoked, Normalize(transferType));
    }

    public void RecordOperatorProfileDecision(OperatorProfileDecisionDto? decision)
    {
        if (decision is null)
        {
            return;
        }

        var rule = Normalize(decision.RuleId);
        Increment(_operatorProfileEvaluationsByRule, rule);
        if (decision.Applied)
        {
            Increment(_operatorProfileAdjustmentsByRule, rule);
        }
    }

    public void RecordSustainabilityRequest(SustainabilityRequestRecord record)
    {
        var key = new SustainabilityMetricKey(
            Normalize(record.TenantId),
            Normalize(record.NodeId),
            Normalize(record.NodeName),
            Normalize(record.NodeGroup),
            Normalize(record.NodeRole),
            Normalize(record.Status));
        var accumulator = _sustainability.GetOrAdd(key, _ => new SustainabilityMetricAccumulator());
        accumulator.Add(record);
    }

    public OrchestratorMetricsSnapshot Snapshot()
    {
        return new OrchestratorMetricsSnapshot(
            discrepanciesBySeverity: SnapshotDictionary(_discrepanciesBySeverity),
            guardrailViolationsBySeverity: SnapshotDictionary(_guardrailViolationsBySeverity),
            guardrailOutcomes: SnapshotDictionary(_guardrailOutcomes),
            guardrailCorrections: SnapshotDictionary(_guardrailCorrections),
            ownershipTransfersRequested: SnapshotDictionary(_ownershipTransfersRequested),
            ownershipTransfersApproved: SnapshotDictionary(_ownershipTransfersApproved),
            ownershipTransfersRevoked: SnapshotDictionary(_ownershipTransfersRevoked),
            operatorProfileEvaluationsByRule: SnapshotDictionary(_operatorProfileEvaluationsByRule),
            operatorProfileAdjustmentsByRule: SnapshotDictionary(_operatorProfileAdjustmentsByRule),
            sustainability: SnapshotSustainability(),
            simulationInfeasibleTotal: Interlocked.Read(ref _simulationInfeasibleTotal),
            simulationTimeoutTotal: Interlocked.Read(ref _simulationTimeoutTotal));
    }

    private static void Increment(ConcurrentDictionary<string, long> dictionary, string key)
    {
        dictionary.AddOrUpdate(key, 1, (_, value) => value + 1);
    }

    private static IReadOnlyDictionary<string, long> SnapshotDictionary(ConcurrentDictionary<string, long> dictionary)
    {
        return new Dictionary<string, long>(dictionary, StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();
    }

    private IReadOnlyList<SustainabilityAggregateMetricDto> SnapshotSustainability()
    {
        return _sustainability
            .Select(entry =>
            {
                var snapshot = entry.Value.Snapshot();
                return new SustainabilityAggregateMetricDto(
                    entry.Key.TenantId,
                    entry.Key.NodeId,
                    entry.Key.NodeName,
                    entry.Key.NodeGroup,
                    entry.Key.NodeRole,
                    entry.Key.Status,
                    snapshot.RequestsTotal,
                    snapshot.DurationSecondsSum,
                    snapshot.DurationSecondsCount,
                    snapshot.EnergyWhTotal,
                    snapshot.CostUsdTotal,
                    snapshot.WaterLitersTotal,
                    snapshot.CloudBaselineEnergyWhTotal,
                    snapshot.CloudBaselineCostUsdTotal,
                    snapshot.CloudBaselineWaterLitersTotal,
                    snapshot.EnergySavedWhTotal,
                    snapshot.CostSavedUsdTotal,
                    snapshot.WaterSavedLitersTotal);
            })
            .ToArray();
    }

    private sealed record SustainabilityMetricKey(
        string TenantId,
        string NodeId,
        string NodeName,
        string NodeGroup,
        string NodeRole,
        string Status);

    private sealed class SustainabilityMetricAccumulator
    {
        private readonly object _gate = new();
        private long _requestsTotal;
        private long _durationSecondsCount;
        private double _durationSecondsSum;
        private double _energyWhTotal;
        private double _costUsdTotal;
        private double _waterLitersTotal;
        private double _cloudBaselineEnergyWhTotal;
        private double _cloudBaselineCostUsdTotal;
        private double _cloudBaselineWaterLitersTotal;
        private double _energySavedWhTotal;
        private double _costSavedUsdTotal;
        private double _waterSavedLitersTotal;

        public void Add(SustainabilityRequestRecord record)
        {
            lock (_gate)
            {
                _requestsTotal++;
                _durationSecondsCount++;
                _durationSecondsSum += record.DurationSeconds;
                if (record.EnergyWh.HasValue)
                {
                    _energyWhTotal += record.EnergyWh.Value;
                }

                if (record.CostUsd.HasValue)
                {
                    _costUsdTotal += record.CostUsd.Value;
                }

                if (record.WaterLiters.HasValue)
                {
                    _waterLitersTotal += record.WaterLiters.Value;
                }

                if (record.CloudBaselineEnergyWh.HasValue)
                {
                    _cloudBaselineEnergyWhTotal += record.CloudBaselineEnergyWh.Value;
                }

                if (record.CloudBaselineCostUsd.HasValue)
                {
                    _cloudBaselineCostUsdTotal += record.CloudBaselineCostUsd.Value;
                }

                if (record.CloudBaselineWaterLiters.HasValue)
                {
                    _cloudBaselineWaterLitersTotal += record.CloudBaselineWaterLiters.Value;
                }

                if (record.EnergySavedWh.HasValue)
                {
                    _energySavedWhTotal += record.EnergySavedWh.Value;
                }

                if (record.CostSavedUsd.HasValue)
                {
                    _costSavedUsdTotal += record.CostSavedUsd.Value;
                }

                if (record.WaterSavedLiters.HasValue)
                {
                    _waterSavedLitersTotal += record.WaterSavedLiters.Value;
                }
            }
        }

        public SustainabilityMetricAccumulatorSnapshot Snapshot()
        {
            lock (_gate)
            {
                return new SustainabilityMetricAccumulatorSnapshot(
                    _requestsTotal,
                    _durationSecondsSum,
                    _durationSecondsCount,
                    _energyWhTotal,
                    _costUsdTotal,
                    _waterLitersTotal,
                    _cloudBaselineEnergyWhTotal,
                    _cloudBaselineCostUsdTotal,
                    _cloudBaselineWaterLitersTotal,
                    _energySavedWhTotal,
                    _costSavedUsdTotal,
                    _waterSavedLitersTotal);
            }
        }
    }

    private sealed record SustainabilityMetricAccumulatorSnapshot(
        long RequestsTotal,
        double DurationSecondsSum,
        long DurationSecondsCount,
        double EnergyWhTotal,
        double CostUsdTotal,
        double WaterLitersTotal,
        double CloudBaselineEnergyWhTotal,
        double CloudBaselineCostUsdTotal,
        double CloudBaselineWaterLitersTotal,
        double EnergySavedWhTotal,
        double CostSavedUsdTotal,
        double WaterSavedLitersTotal);
}

public sealed class OrchestratorMetricsSnapshot
{
    public OrchestratorMetricsSnapshot(
        IReadOnlyDictionary<string, long> discrepanciesBySeverity,
        IReadOnlyDictionary<string, long> guardrailViolationsBySeverity,
        IReadOnlyDictionary<string, long> guardrailOutcomes,
        IReadOnlyDictionary<string, long> guardrailCorrections,
        IReadOnlyDictionary<string, long> ownershipTransfersRequested,
        IReadOnlyDictionary<string, long> ownershipTransfersApproved,
        IReadOnlyDictionary<string, long> ownershipTransfersRevoked,
        IReadOnlyDictionary<string, long> operatorProfileEvaluationsByRule,
        IReadOnlyDictionary<string, long> operatorProfileAdjustmentsByRule,
        IReadOnlyList<SustainabilityAggregateMetricDto> sustainability,
        long simulationInfeasibleTotal,
        long simulationTimeoutTotal)
    {
        DiscrepanciesBySeverity = discrepanciesBySeverity;
        GuardrailViolationsBySeverity = guardrailViolationsBySeverity;
        GuardrailOutcomes = guardrailOutcomes;
        GuardrailCorrections = guardrailCorrections;
        OwnershipTransfersRequested = ownershipTransfersRequested;
        OwnershipTransfersApproved = ownershipTransfersApproved;
        OwnershipTransfersRevoked = ownershipTransfersRevoked;
        OperatorProfileEvaluationsByRule = operatorProfileEvaluationsByRule;
        OperatorProfileAdjustmentsByRule = operatorProfileAdjustmentsByRule;
        Sustainability = sustainability;
        SimulationInfeasibleTotal = simulationInfeasibleTotal;
        SimulationTimeoutTotal = simulationTimeoutTotal;
    }

    public IReadOnlyDictionary<string, long> DiscrepanciesBySeverity { get; }
    public IReadOnlyDictionary<string, long> GuardrailViolationsBySeverity { get; }
    public IReadOnlyDictionary<string, long> GuardrailOutcomes { get; }
    public IReadOnlyDictionary<string, long> GuardrailCorrections { get; }
    public IReadOnlyDictionary<string, long> OwnershipTransfersRequested { get; }
    public IReadOnlyDictionary<string, long> OwnershipTransfersApproved { get; }
    public IReadOnlyDictionary<string, long> OwnershipTransfersRevoked { get; }
    public IReadOnlyDictionary<string, long> OperatorProfileEvaluationsByRule { get; }
    public IReadOnlyDictionary<string, long> OperatorProfileAdjustmentsByRule { get; }
    public IReadOnlyList<SustainabilityAggregateMetricDto> Sustainability { get; }
    public long SimulationInfeasibleTotal { get; }
    public long SimulationTimeoutTotal { get; }
}
