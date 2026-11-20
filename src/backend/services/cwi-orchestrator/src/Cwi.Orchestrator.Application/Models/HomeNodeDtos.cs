#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class HomeNodeUpsertRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("node_id")]
    public string NodeId { get; init; } = string.Empty;

    [JsonPropertyName("owner_user_id")]
    public string OwnerUserId { get; init; } = string.Empty;

    [JsonPropertyName("device_class")]
    public string? DeviceClass { get; init; }

    [JsonPropertyName("tailscale_ip")]
    public string? TailscaleIp { get; init; }

    [JsonPropertyName("watcher_node_id")]
    public string? WatcherNodeId { get; init; }

    [JsonPropertyName("capabilities")]
    public Dictionary<string, object?> Capabilities { get; init; } = new();

    [JsonPropertyName("pack_versions")]
    public Dictionary<string, object?> PackVersions { get; init; } = new();

    [JsonPropertyName("policy_profile_version")]
    public string? PolicyProfileVersion { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("last_heartbeat_utc")]
    public DateTimeOffset? LastHeartbeatUtc { get; init; }

    [JsonPropertyName("assigned_utc")]
    public DateTimeOffset? AssignedUtc { get; init; }
}

public sealed class HomeNodeDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("node_id")]
    public string NodeId { get; init; } = string.Empty;

    [JsonPropertyName("owner_user_id")]
    public string OwnerUserId { get; init; } = string.Empty;

    [JsonPropertyName("device_class")]
    public string? DeviceClass { get; init; }

    [JsonPropertyName("tailscale_ip")]
    public string? TailscaleIp { get; init; }

    [JsonPropertyName("watcher_node_id")]
    public string? WatcherNodeId { get; init; }

    [JsonPropertyName("capabilities")]
    public IReadOnlyDictionary<string, object?> Capabilities { get; init; } = new Dictionary<string, object?>();

    [JsonPropertyName("pack_versions")]
    public IReadOnlyDictionary<string, object?> PackVersions { get; init; } = new Dictionary<string, object?>();

    [JsonPropertyName("policy_profile_version")]
    public string PolicyProfileVersion { get; init; } = "1.0";

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("last_heartbeat_utc")]
    public DateTimeOffset? LastHeartbeatUtc { get; init; }

    [JsonPropertyName("assigned_utc")]
    public DateTimeOffset? AssignedUtc { get; init; }

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; init; }

    [JsonPropertyName("updated_utc")]
    public DateTimeOffset UpdatedUtc { get; init; }
}

public sealed class HomeNodeAssignmentUpsertRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("primary_home_node_id")]
    public string PrimaryHomeNodeId { get; init; } = string.Empty;

    [JsonPropertyName("watcher_node_id")]
    public string? WatcherNodeId { get; init; }

    [JsonPropertyName("policy_scope_id")]
    public string? PolicyScopeId { get; init; }

    [JsonPropertyName("local_only")]
    public bool LocalOnly { get; init; } = true;

    [JsonPropertyName("remote_fallback_allowed")]
    public bool RemoteFallbackAllowed { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("assigned_utc")]
    public DateTimeOffset? AssignedUtc { get; init; }
}

public sealed class HomeNodeAssignmentDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("primary_home_node_id")]
    public string PrimaryHomeNodeId { get; init; } = string.Empty;

    [JsonPropertyName("watcher_node_id")]
    public string? WatcherNodeId { get; init; }

    [JsonPropertyName("policy_scope_id")]
    public string? PolicyScopeId { get; init; }

    [JsonPropertyName("local_only")]
    public bool LocalOnly { get; init; }

    [JsonPropertyName("remote_fallback_allowed")]
    public bool RemoteFallbackAllowed { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("assigned_utc")]
    public DateTimeOffset AssignedUtc { get; init; }

    [JsonPropertyName("revoked_utc")]
    public DateTimeOffset? RevokedUtc { get; init; }

    [JsonPropertyName("updated_utc")]
    public DateTimeOffset UpdatedUtc { get; init; }
}

public sealed class HomeNodePolicyScopeUpsertRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("policy_scope_id")]
    public string PolicyScopeId { get; init; } = string.Empty;

    [JsonPropertyName("rules")]
    public Dictionary<string, object?> Rules { get; init; } = new();

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

public sealed class HomeNodePolicyScopeDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("policy_scope_id")]
    public string PolicyScopeId { get; init; } = string.Empty;

    [JsonPropertyName("rules")]
    public IReadOnlyDictionary<string, object?> Rules { get; init; } = new Dictionary<string, object?>();

    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0";

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; init; }

    [JsonPropertyName("updated_utc")]
    public DateTimeOffset UpdatedUtc { get; init; }
}

public sealed class CurrentUserHomeNodeLinkRequestDto
{
    [JsonPropertyName("node_id")]
    public string? NodeId { get; init; }

    [JsonPropertyName("tailscale_ip")]
    public string? TailscaleIp { get; init; }

    [JsonPropertyName("qr_code_payload")]
    public string? QrCodePayload { get; init; }

    [JsonPropertyName("watcher_node_id")]
    public string? WatcherNodeId { get; init; }

    [JsonPropertyName("policy_scope_id")]
    public string? PolicyScopeId { get; init; }

    [JsonPropertyName("local_only")]
    public bool LocalOnly { get; init; } = true;

    [JsonPropertyName("remote_fallback_allowed")]
    public bool RemoteFallbackAllowed { get; init; }
}

public sealed class CurrentUserHomeNodeLinkDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("linked")]
    public bool Linked { get; init; }

    [JsonPropertyName("matched_identifier")]
    public string? MatchedIdentifier { get; init; }

    [JsonPropertyName("matched_via")]
    public string? MatchedVia { get; init; }

    [JsonPropertyName("assignment")]
    public HomeNodeAssignmentDto? Assignment { get; init; }

    [JsonPropertyName("node")]
    public HomeNodeDto? Node { get; init; }
}

public sealed class AdminHomeNodeUserLinkDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("linked")]
    public bool Linked { get; init; }

    [JsonPropertyName("node_id")]
    public string? NodeId { get; init; }

    [JsonPropertyName("node_status")]
    public string? NodeStatus { get; init; }

    [JsonPropertyName("tailscale_ip")]
    public string? TailscaleIp { get; init; }

    [JsonPropertyName("assignment_status")]
    public string? AssignmentStatus { get; init; }

    [JsonPropertyName("assigned_utc")]
    public DateTimeOffset? AssignedUtc { get; init; }

    [JsonPropertyName("last_heartbeat_utc")]
    public DateTimeOffset? LastHeartbeatUtc { get; init; }

    [JsonPropertyName("policy_scope_id")]
    public string? PolicyScopeId { get; init; }

    [JsonPropertyName("local_only")]
    public bool LocalOnly { get; init; }

    [JsonPropertyName("remote_fallback_allowed")]
    public bool RemoteFallbackAllowed { get; init; }
}
#nullable disable
