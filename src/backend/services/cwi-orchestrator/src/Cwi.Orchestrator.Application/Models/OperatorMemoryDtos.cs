#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class OperatorMemoryIngestRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("operator_id")]
    public string OperatorId { get; init; } = string.Empty;

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("consent")]
    public OperatorConsentDto? Consent { get; init; }

    [JsonPropertyName("signals")]
    public IReadOnlyList<OperatorSignalDto> Signals { get; init; } = Array.Empty<OperatorSignalDto>();

    [JsonPropertyName("performance")]
    public IReadOnlyList<OperatorPerformanceDto> Performance { get; init; } = Array.Empty<OperatorPerformanceDto>();

    [JsonPropertyName("metadata")]
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed class OperatorConsentDto
{
    [JsonPropertyName("granted")]
    public bool Granted { get; init; }

    [JsonPropertyName("granted_utc")]
    public DateTimeOffset? GrantedUtc { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }
}

public sealed class OperatorSignalDto
{
    [JsonPropertyName("signal_type")]
    public string SignalType { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public double Value { get; init; }

    [JsonPropertyName("unit")]
    public string Unit { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    [JsonPropertyName("occurred_utc")]
    public DateTimeOffset OccurredUtc { get; init; }
}

public sealed class OperatorPerformanceDto
{
    [JsonPropertyName("metric")]
    public string Metric { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public double Value { get; init; }

    [JsonPropertyName("unit")]
    public string Unit { get; init; } = string.Empty;

    [JsonPropertyName("window_utc")]
    public DateTimeOffset WindowUtc { get; init; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }
}

public sealed class OperatorMemoryIngestResponseDto
{
    [JsonPropertyName("batch_id")]
    public string BatchId { get; init; } = string.Empty;

    [JsonPropertyName("accepted")]
    public int Accepted { get; init; }

    [JsonPropertyName("rejected")]
    public int Rejected { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("profile")]
    public OperatorProfileSummaryDto? Profile { get; init; }
}

public sealed class OperatorProfileSummaryDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("operator_id")]
    public string OperatorId { get; init; } = string.Empty;

    [JsonPropertyName("signals_total")]
    public int SignalsTotal { get; init; }

    [JsonPropertyName("correction_rate")]
    public double CorrectionRate { get; init; }

    [JsonPropertyName("delay_rate")]
    public double DelayRate { get; init; }

    [JsonPropertyName("confirmation_rate")]
    public double ConfirmationRate { get; init; }

    [JsonPropertyName("average_latency_ms")]
    public double? AverageLatencyMs { get; init; }

    [JsonPropertyName("correlation_score")]
    public double CorrelationScore { get; init; }

    [JsonPropertyName("last_updated_utc")]
    public DateTimeOffset LastUpdatedUtc { get; init; }
}

public sealed class OperatorProfileDecisionDto
{
    [JsonPropertyName("applied")]
    public bool Applied { get; init; }

    [JsonPropertyName("policy_mode")]
    public string PolicyMode { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("rule_id")]
    public string RuleId { get; init; } = string.Empty;
}
#nullable disable
