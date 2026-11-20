#nullable enable
namespace Cwi.Orchestrator.Application.Models;

public sealed class SustainabilityRequestRecordDto
{
    public string RequestId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; }
    public string TenantId { get; init; } = string.Empty;
    public string UserIdHash { get; init; } = string.Empty;
    public string UserRole { get; init; } = string.Empty;
    public string NodeId { get; init; } = string.Empty;
    public string NodeName { get; init; } = string.Empty;
    public string NodeGroup { get; init; } = string.Empty;
    public string NodeRole { get; init; } = string.Empty;
    public double? NodeWatts { get; init; }
    public string WattsSource { get; init; } = string.Empty;
    public string? PrometheusInstance { get; init; }
    public string TelemetrySource { get; init; } = string.Empty;
    public string CalculationVersion { get; init; } = string.Empty;
    public double DurationSeconds { get; init; }
    public double? EnergyWh { get; init; }
    public string EnergyValueType { get; init; } = string.Empty;
    public double? CostUsd { get; init; }
    public string CostValueType { get; init; } = string.Empty;
    public double? WaterLiters { get; init; }
    public string WaterValueType { get; init; } = string.Empty;
    public string BaselineModelVersion { get; init; } = string.Empty;
    public double? CloudBaselineEnergyWh { get; init; }
    public string CloudBaselineEnergyValueType { get; init; } = string.Empty;
    public double? CloudBaselineCostUsd { get; init; }
    public string CloudBaselineCostValueType { get; init; } = string.Empty;
    public double? CloudBaselineWaterLiters { get; init; }
    public string CloudBaselineWaterValueType { get; init; } = string.Empty;
    public double? EnergySavedWh { get; init; }
    public double? CostSavedUsd { get; init; }
    public double? WaterSavedLiters { get; init; }
    public string SavingsValueType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string ModelOrRuntime { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
}

public sealed record SustainabilityAggregateMetricDto(
    string TenantId,
    string NodeId,
    string NodeName,
    string NodeGroup,
    string NodeRole,
    string Status,
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

public sealed class SustainabilityMetricsOptions
{
    public const double DefaultEstimatedNodeWatts = 60.0d;
    public const double DefaultCostUsdPerKwh = 0.12d;
    public const double DefaultEstimatedWaterLitersPerRequest = 0.005d;
    public const double DefaultCloudBaselineEnergyWhPerRequest = 3.0d;
    public const double DefaultCloudBaselineCostUsdPerRequest = 0.001d;
    public const double DefaultCloudBaselineWaterLitersPerRequest = 0.02d;
    public const string DefaultCloudBaselineModelVersion = "cwi-sustainability-seeded-estimates.v1";
    public const string DefaultCalculationVersion = "cwi-sustainability-estimates.v1";

    public double? EstimatedNodeWatts { get; init; }
    public double? CostUsdPerKwh { get; init; }
    public double? EstimatedWaterLitersPerRequest { get; init; }
    public string? CloudBaselineModelVersion { get; init; }
    public double? CloudBaselineEnergyWhPerRequest { get; init; }
    public double? CloudBaselineCostUsdPerRequest { get; init; }
    public double? CloudBaselineWaterLitersPerRequest { get; init; }

    public static SustainabilityMetricsOptions LoadFromEnvironment()
    {
        return new SustainabilityMetricsOptions
        {
            EstimatedNodeWatts = ReadPositiveDouble("CWI_SUSTAINABILITY_ESTIMATED_NODE_WATTS") ?? DefaultEstimatedNodeWatts,
            CostUsdPerKwh = ReadPositiveDouble("CWI_SUSTAINABILITY_COST_USD_PER_KWH") ?? DefaultCostUsdPerKwh,
            EstimatedWaterLitersPerRequest = ReadPositiveDouble("CWI_SUSTAINABILITY_ESTIMATED_WATER_LITERS_PER_REQUEST") ?? DefaultEstimatedWaterLitersPerRequest,
            CloudBaselineModelVersion = ReadOptionalString("CWI_SUSTAINABILITY_CLOUD_BASELINE_VERSION") ?? DefaultCloudBaselineModelVersion,
            CloudBaselineEnergyWhPerRequest = ReadPositiveDouble("CWI_SUSTAINABILITY_CLOUD_ENERGY_WH_PER_REQUEST") ?? DefaultCloudBaselineEnergyWhPerRequest,
            CloudBaselineCostUsdPerRequest = ReadPositiveDouble("CWI_SUSTAINABILITY_CLOUD_COST_USD_PER_REQUEST") ?? DefaultCloudBaselineCostUsdPerRequest,
            CloudBaselineWaterLitersPerRequest = ReadPositiveDouble("CWI_SUSTAINABILITY_CLOUD_WATER_LITERS_PER_REQUEST") ?? DefaultCloudBaselineWaterLitersPerRequest
        };
    }

    private static double? ReadPositiveDouble(string key)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        if (!double.TryParse(raw, out var value) || value <= 0)
        {
            return null;
        }

        return value;
    }

    private static string? ReadOptionalString(string key)
    {
        var raw = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }
}
#nullable disable
