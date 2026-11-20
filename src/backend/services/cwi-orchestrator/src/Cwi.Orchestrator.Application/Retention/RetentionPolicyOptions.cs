using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Retention;

public sealed class RetentionPolicyOptions
{
    [JsonPropertyName("telemetry_days")]
    public int TelemetryDays { get; init; }

    [JsonPropertyName("ledger_days")]
    public int LedgerDays { get; init; }

    [JsonPropertyName("trace_days")]
    public int TraceDays { get; init; }

    [JsonPropertyName("purge_enabled")]
    public bool PurgeEnabled { get; init; }
}
