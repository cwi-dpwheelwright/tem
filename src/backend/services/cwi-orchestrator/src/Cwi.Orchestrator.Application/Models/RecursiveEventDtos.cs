#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class RecursiveEventIngestResponseDto
{
    [JsonPropertyName("accepted")]
    public bool Accepted { get; init; }

    [JsonPropertyName("quarantined")]
    public bool Quarantined { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("event_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EventId { get; init; }

    [JsonPropertyName("quarantine_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? QuarantineId { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    [JsonPropertyName("trace_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TraceId { get; init; }

    [JsonPropertyName("temporal_state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TemporalState { get; init; }

    [JsonPropertyName("recorded_utc")]
    public DateTimeOffset RecordedUtc { get; init; }
}

public sealed class RecursiveEventRecordDto
{
    [JsonPropertyName("event_id")]
    public string EventId { get; init; } = string.Empty;

    [JsonPropertyName("event_type")]
    public string EventType { get; init; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("scope_type")]
    public string ScopeType { get; init; } = string.Empty;

    [JsonPropertyName("source_component")]
    public string SourceComponent { get; init; } = string.Empty;

    [JsonPropertyName("source_type")]
    public string SourceType { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("trace_id")]
    public string TraceId { get; init; } = string.Empty;

    [JsonPropertyName("policy_status")]
    public string PolicyStatus { get; init; } = string.Empty;

    [JsonPropertyName("temporal_state")]
    public string TemporalState { get; init; } = string.Empty;

    [JsonPropertyName("timestamp_utc")]
    public DateTimeOffset TimestampUtc { get; init; }

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; init; }
}
#nullable disable
