#nullable enable
namespace Cwi.Orchestrator.Domain.Entities;

public sealed class SustainabilityRequestRecord
{
    public Guid Id { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UserIdHash { get; set; } = string.Empty;
    public string UserRole { get; set; } = "unknown";
    public string NodeId { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
    public string NodeGroup { get; set; } = string.Empty;
    public string NodeRole { get; set; } = string.Empty;
    public double? NodeWatts { get; set; }
    public string WattsSource { get; set; } = "unknown";
    public string? PrometheusInstance { get; set; }
    public string TelemetrySource { get; set; } = "unknown";
    public string CalculationVersion { get; set; } = "unknown";
    public double DurationSeconds { get; set; }
    public double? EnergyWh { get; set; }
    public string EnergyValueType { get; set; } = "unknown";
    public double? CostUsd { get; set; }
    public string CostValueType { get; set; } = "unknown";
    public double? WaterLiters { get; set; }
    public string WaterValueType { get; set; } = "unknown";
    public string BaselineModelVersion { get; set; } = "none";
    public double? CloudBaselineEnergyWh { get; set; }
    public string CloudBaselineEnergyValueType { get; set; } = "unknown";
    public double? CloudBaselineCostUsd { get; set; }
    public string CloudBaselineCostValueType { get; set; } = "unknown";
    public double? CloudBaselineWaterLiters { get; set; }
    public string CloudBaselineWaterValueType { get; set; } = "unknown";
    public double? EnergySavedWh { get; set; }
    public double? CostSavedUsd { get; set; }
    public double? WaterSavedLiters { get; set; }
    public string SavingsValueType { get; set; } = "unknown";
    public string Status { get; set; } = string.Empty;
    public string ModelOrRuntime { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
}
#nullable disable
