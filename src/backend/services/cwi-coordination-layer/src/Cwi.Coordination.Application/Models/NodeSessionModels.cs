using System.Text.Json.Serialization;

namespace Cwi.Coordination.Application.Models;

public sealed class NodeHelloMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "node_hello";

    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("owner_user_id")]
    public string OwnerUserId { get; set; } = string.Empty;

    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; set; }

    [JsonPropertyName("runtime_version")]
    public string? RuntimeVersion { get; set; }

    [JsonPropertyName("policy_profile_version")]
    public string? PolicyProfileVersion { get; set; }

    [JsonPropertyName("capabilities")]
    public List<string> Capabilities { get; set; } = [];

    [JsonPropertyName("capability_details")]
    public Dictionary<string, object?> CapabilityDetails { get; set; } = new();
}

public sealed class NodeKeepaliveMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "keepalive";

    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("ts")]
    public string? Timestamp { get; set; }
}

public sealed class NodeHeartbeatMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "heartbeat";

    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("owner_user_id")]
    public string? OwnerUserId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "READY";

    [JsonPropertyName("cpu_summary")]
    public Dictionary<string, object?> CpuSummary { get; set; } = new();

    [JsonPropertyName("memory_summary")]
    public Dictionary<string, object?> MemorySummary { get; set; } = new();

    [JsonPropertyName("runtime_version")]
    public string? RuntimeVersion { get; set; }

    [JsonPropertyName("policy_profile_version")]
    public string? PolicyProfileVersion { get; set; }

    [JsonPropertyName("active_session_count")]
    public int? ActiveSessionCount { get; set; }

    [JsonPropertyName("local_privacy_mode")]
    public List<string> LocalPrivacyMode { get; set; } = [];

    [JsonPropertyName("model_profile")]
    public Dictionary<string, object?> ModelProfile { get; set; } = new();

    [JsonPropertyName("hardware")]
    public Dictionary<string, object?> Hardware { get; set; } = new();
}

public sealed class NodeWorkTraceContext
{
    [JsonPropertyName("trace_id")]
    public string? TraceId { get; set; }

    [JsonPropertyName("request_origin")]
    public string? RequestOrigin { get; set; }
}

public sealed class NodeWorkEnvelopeMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "work_envelope";

    [JsonPropertyName("work_id")]
    public string WorkId { get; set; } = string.Empty;

    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    [JsonPropertyName("lease_id")]
    public string LeaseId { get; set; } = string.Empty;

    [JsonPropertyName("request_kind")]
    public string RequestKind { get; set; } = string.Empty;

    [JsonPropertyName("task_type")]
    public string TaskType { get; set; } = string.Empty;

    [JsonPropertyName("target_node_id")]
    public string TargetNodeId { get; set; } = string.Empty;

    [JsonPropertyName("policy_scope")]
    public string? PolicyScope { get; set; }

    [JsonPropertyName("trace_context")]
    public NodeWorkTraceContext TraceContext { get; set; } = new();

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("payload")]
    public Dictionary<string, object?> Payload { get; set; } = new();
}

public sealed class NodeWorkAckMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "work_ack";

    [JsonPropertyName("work_id")]
    public string WorkId { get; set; } = string.Empty;

    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("lease_id")]
    public string LeaseId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "ACCEPTED";
}

public sealed class NodeWorkResultMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "work_result";

    [JsonPropertyName("work_id")]
    public string WorkId { get; set; } = string.Empty;

    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("lease_id")]
    public string LeaseId { get; set; } = string.Empty;

    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = "SUCCESS";

    [JsonPropertyName("result")]
    public Dictionary<string, object?> Result { get; set; } = new();

    [JsonPropertyName("timing_ms")]
    public Dictionary<string, object?> TimingMs { get; set; } = new();
}

public sealed class NodeSessionSnapshot
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("owner_user_id")]
    public string OwnerUserId { get; set; } = string.Empty;

    [JsonPropertyName("friendly_name")]
    public string? FriendlyName { get; set; }

    [JsonPropertyName("runtime_version")]
    public string? RuntimeVersion { get; set; }

    [JsonPropertyName("policy_profile_version")]
    public string? PolicyProfileVersion { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "connected";

    [JsonPropertyName("connected_utc")]
    public DateTimeOffset ConnectedUtc { get; set; }

    [JsonPropertyName("last_seen_utc")]
    public DateTimeOffset LastSeenUtc { get; set; }

    [JsonPropertyName("last_keepalive_utc")]
    public DateTimeOffset? LastKeepaliveUtc { get; set; }

    [JsonPropertyName("last_heartbeat_utc")]
    public DateTimeOffset? LastHeartbeatUtc { get; set; }
}
