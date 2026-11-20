using System.Text.Json.Serialization;

namespace Cwi.Coordination.Application.Models;

public sealed class LinkStateUpdateRequest
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("interface")]
    public string Interface { get; set; } = "unknown";

    [JsonPropertyName("signal_quality")]
    public double SignalQuality { get; set; }

    [JsonPropertyName("latency_ms")]
    public int LatencyMs { get; set; }

    [JsonPropertyName("jitter_ms")]
    public double? JitterMs { get; set; }

    [JsonPropertyName("packet_loss")]
    public double PacketLoss { get; set; }

    [JsonPropertyName("bandwidth_kbps")]
    public int BandwidthKbps { get; set; }

    [JsonPropertyName("throughput_kbps")]
    public int ThroughputKbps { get; set; }

    [JsonPropertyName("rssi_dbm")]
    public double? RssiDbm { get; set; }

    [JsonPropertyName("snr_db")]
    public double? SnrDb { get; set; }
}

public sealed class LinkStateResponse
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("interface")]
    public string Interface { get; set; } = string.Empty;

    [JsonPropertyName("signal_quality")]
    public double SignalQuality { get; set; }

    [JsonPropertyName("latency_ms")]
    public int LatencyMs { get; set; }

    [JsonPropertyName("jitter_ms")]
    public double? JitterMs { get; set; }

    [JsonPropertyName("packet_loss")]
    public double PacketLoss { get; set; }

    [JsonPropertyName("bandwidth_kbps")]
    public int BandwidthKbps { get; set; }

    [JsonPropertyName("throughput_kbps")]
    public int ThroughputKbps { get; set; }

    [JsonPropertyName("rssi_dbm")]
    public double? RssiDbm { get; set; }

    [JsonPropertyName("snr_db")]
    public double? SnrDb { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("updated_utc")]
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class RoutingHintRequest
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("task_complexity")]
    public string TaskComplexity { get; set; } = "medium";
}

public sealed class RoutingHintResponse
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("preferred_interface")]
    public string PreferredInterface { get; set; } = string.Empty;

    [JsonPropertyName("preferred_path")]
    public string? PreferredPath { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("fallback_interfaces")]
    public IReadOnlyList<string> FallbackInterfaces { get; set; } = Array.Empty<string>();

    [JsonPropertyName("ttl_seconds")]
    public int TtlSeconds { get; set; }

    [JsonPropertyName("cooldown_seconds")]
    public int CooldownSeconds { get; set; }

    [JsonPropertyName("hysteresis_margin")]
    public double HysteresisMargin { get; set; }
}

public sealed class BackhaulPolicyRequest
{
    [JsonPropertyName("require_authenticated_channel")]
    public bool RequireAuthenticatedChannel { get; set; } = true;

    [JsonPropertyName("allowed_interfaces")]
    public IReadOnlyList<string> AllowedInterfaces { get; set; } = Array.Empty<string>();

    [JsonPropertyName("denied_interfaces")]
    public IReadOnlyList<string> DeniedInterfaces { get; set; } = Array.Empty<string>();

    [JsonPropertyName("revoked_peers")]
    public IReadOnlyList<string> RevokedPeers { get; set; } = Array.Empty<string>();

    [JsonPropertyName("policy_version")]
    public string PolicyVersion { get; set; } = "1.0";
}

public sealed class BackhaulPolicyResponse
{
    [JsonPropertyName("require_authenticated_channel")]
    public bool RequireAuthenticatedChannel { get; set; }

    [JsonPropertyName("allowed_interfaces")]
    public IReadOnlyList<string> AllowedInterfaces { get; set; } = Array.Empty<string>();

    [JsonPropertyName("denied_interfaces")]
    public IReadOnlyList<string> DeniedInterfaces { get; set; } = Array.Empty<string>();

    [JsonPropertyName("revoked_peers")]
    public IReadOnlyList<string> RevokedPeers { get; set; } = Array.Empty<string>();

    [JsonPropertyName("policy_version")]
    public string PolicyVersion { get; set; } = "1.0";
}

public sealed class BackhaulDecisionRequest
{
    [JsonPropertyName("interface")]
    public string Interface { get; set; } = "unknown";

    [JsonPropertyName("peer_id")]
    public string? PeerId { get; set; }

    [JsonPropertyName("authenticated")]
    public bool Authenticated { get; set; }
}

public sealed class BackhaulDecisionResponse
{
    [JsonPropertyName("allowed")]
    public bool Allowed { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

public sealed class SurvivalModeRequest
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

public sealed class SurvivalModeResponse
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("updated_utc")]
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class MeshEventResponse
{
    [JsonPropertyName("event_id")]
    public Guid EventId { get; set; }

    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("interface")]
    public string Interface { get; set; } = string.Empty;

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; set; }
}

public sealed class TopologyHealthResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "unknown";

    [JsonPropertyName("total_nodes")]
    public int TotalNodes { get; set; }

    [JsonPropertyName("degraded_nodes")]
    public int DegradedNodes { get; set; }

    [JsonPropertyName("down_nodes")]
    public int DownNodes { get; set; }

    [JsonPropertyName("total_links")]
    public int TotalLinks { get; set; }

    [JsonPropertyName("degraded_links")]
    public int DegradedLinks { get; set; }

    [JsonPropertyName("down_links")]
    public int DownLinks { get; set; }

    [JsonPropertyName("nodes")]
    public IReadOnlyList<NodeHealthResponse> Nodes { get; set; } = Array.Empty<NodeHealthResponse>();

    [JsonPropertyName("generated_utc")]
    public DateTimeOffset GeneratedUtc { get; set; }
}

public sealed class NodeHealthResponse
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "unknown";

    [JsonPropertyName("link_count")]
    public int LinkCount { get; set; }

    [JsonPropertyName("degraded_links")]
    public int DegradedLinks { get; set; }

    [JsonPropertyName("down_links")]
    public int DownLinks { get; set; }
}

public sealed class RerouteGuidanceRequest
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("current_interface")]
    public string? CurrentInterface { get; set; }

    [JsonPropertyName("task_complexity")]
    public string? TaskComplexity { get; set; }
}

public sealed class RerouteGuidanceResponse
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("reroute_required")]
    public bool RerouteRequired { get; set; }

    [JsonPropertyName("preferred_interface")]
    public string PreferredInterface { get; set; } = string.Empty;

    [JsonPropertyName("fallback_interfaces")]
    public IReadOnlyList<string> FallbackInterfaces { get; set; } = Array.Empty<string>();

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("generated_utc")]
    public DateTimeOffset GeneratedUtc { get; set; }
}
