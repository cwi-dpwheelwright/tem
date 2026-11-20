using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class HomeNodeAskDispatchDecision
{
    public bool ContinueSharedPath { get; init; } = true;
}

internal sealed class CreateHomeNodeWorkRequest
{
    [JsonPropertyName("work_id")]
    public string WorkId { get; init; } = string.Empty;

    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; init; }

    [JsonPropertyName("node_id")]
    public string NodeId { get; init; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("task_type")]
    public string TaskType { get; init; } = "ask.execute";

    [JsonPropertyName("policy_scope")]
    public string? PolicyScope { get; init; }

    [JsonPropertyName("payload")]
    public Dictionary<string, object?> Payload { get; init; } = new();

    [JsonPropertyName("work_token")]
    public string WorkToken { get; init; } = string.Empty;

    [JsonPropertyName("idempotency_key")]
    public string IdempotencyKey { get; init; } = string.Empty;
}

internal sealed class HomeNodeWorkReceipt
{
    [JsonPropertyName("work_id")]
    public string WorkId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("lease_id")]
    public string? LeaseId { get; init; }

    [JsonPropertyName("result")]
    public Dictionary<string, object?> Result { get; init; } = new();

    [JsonPropertyName("completed_utc")]
    public DateTimeOffset? CompletedUtc { get; init; }
}
