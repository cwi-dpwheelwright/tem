using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class OutcomeFeedbackRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("trace_id")]
    public string TraceId { get; init; } = string.Empty;

    [JsonPropertyName("ticket_id")]
    public string? TicketId { get; init; }

    [JsonPropertyName("action_id")]
    public string? ActionId { get; init; }

    [JsonPropertyName("actor_id")]
    public string ActorId { get; init; } = string.Empty;

    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; init; }

    [JsonPropertyName("feedback")]
    public string Feedback { get; init; } = string.Empty;

    [JsonPropertyName("consent_granted")]
    public bool ConsentGranted { get; init; }

    [JsonPropertyName("reason_codes")]
    public IReadOnlyList<string> ReasonCodes { get; init; } = Array.Empty<string>();

    [JsonPropertyName("note")]
    public string? Note { get; init; }

    [JsonPropertyName("confidence_before")]
    public double? ConfidenceBefore { get; init; }

    [JsonPropertyName("confidence_after")]
    public double? ConfidenceAfter { get; init; }
}

public sealed class OutcomeFeedbackResponseDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("trace_id")]
    public string TraceId { get; init; } = string.Empty;

    [JsonPropertyName("ticket_id")]
    public string? TicketId { get; init; }

    [JsonPropertyName("action_id")]
    public string? ActionId { get; init; }

    [JsonPropertyName("actor_id")]
    public string ActorId { get; init; } = string.Empty;

    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; init; }

    [JsonPropertyName("feedback")]
    public string Feedback { get; init; } = string.Empty;

    [JsonPropertyName("consent_granted")]
    public bool ConsentGranted { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }

    [JsonPropertyName("confidence_before")]
    public double? ConfidenceBefore { get; init; }

    [JsonPropertyName("confidence_after")]
    public double? ConfidenceAfter { get; init; }

    [JsonPropertyName("recorded_utc")]
    public DateTime RecordedUtc { get; init; }

    [JsonPropertyName("summary")]
    public OutcomeFeedbackSummaryDto Summary { get; init; } = new();
}

public sealed class OutcomeFeedbackSummaryDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("trace_id")]
    public string TraceId { get; init; } = string.Empty;

    [JsonPropertyName("up_count")]
    public int UpCount { get; init; }

    [JsonPropertyName("down_count")]
    public int DownCount { get; init; }

    [JsonPropertyName("last_feedback")]
    public string? LastFeedback { get; init; }

    [JsonPropertyName("last_feedback_utc")]
    public DateTime? LastFeedbackUtc { get; init; }
}
