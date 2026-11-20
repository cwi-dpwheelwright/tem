using System.Text.Json.Serialization;

namespace Cwi.Coordination.Application.Models;

public sealed class CreateNodeWorkRequest
{
    [JsonPropertyName("work_id")]
    public string? WorkId { get; set; }

    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; set; }

    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("task_type")]
    public string TaskType { get; set; } = string.Empty;

    [JsonPropertyName("policy_scope")]
    public string? PolicyScope { get; set; }

    [JsonPropertyName("payload")]
    public Dictionary<string, object?> Payload { get; set; } = new();

    [JsonPropertyName("work_token")]
    public string? WorkToken { get; set; }

    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; set; }

    [JsonPropertyName("available_utc")]
    public DateTimeOffset? AvailableUtc { get; set; }

    [JsonPropertyName("expires_utc")]
    public DateTimeOffset? ExpiresUtc { get; set; }
}

public sealed class LeaseNodeWorkRequest
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("max_items")]
    public int MaxItems { get; set; } = 1;

    [JsonPropertyName("lease_duration_seconds")]
    public int LeaseDurationSeconds { get; set; } = 60;
}

public sealed class AckNodeWorkRequest
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("lease_id")]
    public string LeaseId { get; set; } = string.Empty;
}

public sealed class CompleteNodeWorkRequest
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("lease_id")]
    public string LeaseId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public Dictionary<string, object?> Result { get; set; } = new();
}

public sealed class NodeWorkItemResponse
{
    [JsonPropertyName("work_id")]
    public string WorkId { get; set; } = string.Empty;

    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; set; }

    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("task_type")]
    public string TaskType { get; set; } = string.Empty;

    [JsonPropertyName("policy_scope")]
    public string? PolicyScope { get; set; }

    [JsonPropertyName("payload")]
    public Dictionary<string, object?> Payload { get; set; } = new();

    [JsonPropertyName("work_token")]
    public string? WorkToken { get; set; }

    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; set; }

    [JsonPropertyName("available_utc")]
    public DateTimeOffset AvailableUtc { get; set; }

    [JsonPropertyName("expires_utc")]
    public DateTimeOffset? ExpiresUtc { get; set; }

    [JsonPropertyName("lease_id")]
    public string? LeaseId { get; set; }

    [JsonPropertyName("lease_expires_utc")]
    public DateTimeOffset? LeaseExpiresUtc { get; set; }

    [JsonPropertyName("lease_count")]
    public int LeaseCount { get; set; }

    [JsonPropertyName("result")]
    public Dictionary<string, object?> Result { get; set; } = new();

    [JsonPropertyName("completed_utc")]
    public DateTimeOffset? CompletedUtc { get; set; }
}

public sealed class LeaseNodeWorkResponse
{
    [JsonPropertyName("items")]
    public IReadOnlyList<NodeWorkItemResponse> Items { get; set; } = Array.Empty<NodeWorkItemResponse>();
}
