#nullable enable
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Telemetry;

public sealed class SustainabilityCalculator
{
    private readonly SustainabilityMetricsOptions _options;

    public SustainabilityCalculator(SustainabilityMetricsOptions options)
    {
        _options = options;
    }

    public SustainabilityCalculation Calculate(
        double durationSeconds,
        double? nodeWatts = null,
        string? wattsSource = null)
    {
        var normalizedDurationSeconds = Math.Max(0.0d, durationSeconds);
        var resolvedNodeWatts = ResolveNodeWatts(nodeWatts);
        var resolvedWattsSource = FirstNonEmpty(wattsSource, nodeWatts.HasValue ? "node_attribution" : "fallback");
        var valueType = ResolveValueType(resolvedWattsSource);
        var energy = EstimateEnergyWh(normalizedDurationSeconds, resolvedNodeWatts);
        var cost = EstimateCostUsd(energy);
        var water = EstimateWaterLiters();
        double? cloudEnergy = _options.CloudBaselineEnergyWhPerRequest ?? SustainabilityMetricsOptions.DefaultCloudBaselineEnergyWhPerRequest;
        double? cloudCost = _options.CloudBaselineCostUsdPerRequest ?? SustainabilityMetricsOptions.DefaultCloudBaselineCostUsdPerRequest;
        double? cloudWater = _options.CloudBaselineWaterLitersPerRequest ?? SustainabilityMetricsOptions.DefaultCloudBaselineWaterLitersPerRequest;

        return new SustainabilityCalculation(
            NodeWatts: resolvedNodeWatts,
            WattsSource: resolvedWattsSource,
            CalculationVersion: SustainabilityMetricsOptions.DefaultCalculationVersion,
            EnergyWh: energy,
            EnergyValueType: valueType,
            CostUsd: cost,
            CostValueType: cost.HasValue ? "estimated" : "unknown",
            WaterLiters: water,
            WaterValueType: water.HasValue ? "estimated" : "unknown",
            BaselineModelVersion: ResolveBaselineModelVersion(cloudEnergy, cloudCost, cloudWater),
            CloudBaselineEnergyWh: cloudEnergy,
            CloudBaselineEnergyValueType: cloudEnergy.HasValue ? "estimated" : "unknown",
            CloudBaselineCostUsd: cloudCost,
            CloudBaselineCostValueType: cloudCost.HasValue ? "estimated" : "unknown",
            CloudBaselineWaterLiters: cloudWater,
            CloudBaselineWaterValueType: cloudWater.HasValue ? "estimated" : "unknown",
            EnergySavedWh: EstimateSavings(cloudEnergy, energy),
            CostSavedUsd: EstimateSavings(cloudCost, cost),
            WaterSavedLiters: EstimateSavings(cloudWater, water));
    }

    private static double? EstimateEnergyWh(
        double durationSeconds,
        double? nodeWatts)
    {
        if (!nodeWatts.HasValue)
        {
            return null;
        }

        return Math.Round(nodeWatts.Value * durationSeconds / 3600.0d, 9);
    }

    private double? EstimateCostUsd(double? energyWh)
    {
        var costUsdPerKwh = _options.CostUsdPerKwh ?? SustainabilityMetricsOptions.DefaultCostUsdPerKwh;
        if (!energyWh.HasValue)
        {
            return null;
        }

        return Math.Round((energyWh.Value / 1000.0d) * costUsdPerKwh, 9);
    }

    private double? EstimateWaterLiters()
    {
        var estimatedWaterLiters = _options.EstimatedWaterLitersPerRequest
            ?? SustainabilityMetricsOptions.DefaultEstimatedWaterLitersPerRequest;
        return Math.Round(estimatedWaterLiters, 9);
    }

    private double? ResolveNodeWatts(double? nodeWatts)
    {
        if (nodeWatts.HasValue && nodeWatts.Value > 0.0d)
        {
            return nodeWatts.Value;
        }

        return _options.EstimatedNodeWatts ?? SustainabilityMetricsOptions.DefaultEstimatedNodeWatts;
    }

    private static string ResolveValueType(string wattsSource)
    {
        return wattsSource.Contains("prometheus", StringComparison.OrdinalIgnoreCase)
            || wattsSource.Contains("telemetry", StringComparison.OrdinalIgnoreCase)
            ? "actual"
            : "estimated";
    }

    private static double? EstimateSavings(double? baseline, double? actual)
    {
        if (!baseline.HasValue || !actual.HasValue)
        {
            return null;
        }

        return Math.Round(Math.Max(0.0d, baseline.Value - actual.Value), 9);
    }

    private string ResolveBaselineModelVersion(
        double? cloudEnergy,
        double? cloudCost,
        double? cloudWater)
    {
        if (!cloudEnergy.HasValue && !cloudCost.HasValue && !cloudWater.HasValue)
        {
            return "none";
        }

        return FirstNonEmpty(
            _options.CloudBaselineModelVersion,
            SustainabilityMetricsOptions.DefaultCloudBaselineModelVersion);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }
}

public sealed record SustainabilityCalculation(
    double? NodeWatts,
    string WattsSource,
    string CalculationVersion,
    double? EnergyWh,
    string EnergyValueType,
    double? CostUsd,
    string CostValueType,
    double? WaterLiters,
    string WaterValueType,
    string BaselineModelVersion,
    double? CloudBaselineEnergyWh,
    string CloudBaselineEnergyValueType,
    double? CloudBaselineCostUsd,
    string CloudBaselineCostValueType,
    double? CloudBaselineWaterLiters,
    string CloudBaselineWaterValueType,
    double? EnergySavedWh,
    double? CostSavedUsd,
    double? WaterSavedLiters)
{
    public string SavingsValueType =>
        EnergySavedWh.HasValue || CostSavedUsd.HasValue || WaterSavedLiters.HasValue
            ? "estimated"
            : "unknown";
}
#nullable disable
