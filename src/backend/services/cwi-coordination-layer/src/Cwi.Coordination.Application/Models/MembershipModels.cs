using System.Text.Json.Serialization;

namespace Cwi.Coordination.Application.Models;

public sealed class EnrollmentRequest
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("owner_user_id")]
    public string OwnerUserId { get; set; } = string.Empty;

    [JsonPropertyName("device_class")]
    public string? DeviceClass { get; set; }

    [JsonPropertyName("tailscale_ip")]
    public string? TailscaleIp { get; set; }

    [JsonPropertyName("watcher_node_id")]
    public string? WatcherNodeId { get; set; }

    [JsonPropertyName("capabilities")]
    public Dictionary<string, object?> Capabilities { get; set; } = new();

    [JsonPropertyName("pack_versions")]
    public Dictionary<string, object?> PackVersions { get; set; } = new();

    [JsonPropertyName("policy_profile_version")]
    public string PolicyProfileVersion { get; set; } = "1.0";

    [JsonPropertyName("health")]
    public Dictionary<string, object?> Health { get; set; } = new();

    [JsonPropertyName("cert_fingerprint")]
    public string CertificateFingerprint { get; set; } = string.Empty;

    [JsonPropertyName("protocol_version")]
    public string ProtocolVersion { get; set; } = "1.0";
}

public sealed class EnrollmentResponse
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

public sealed class MemberResponse
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("owner_user_id")]
    public string OwnerUserId { get; set; } = string.Empty;

    [JsonPropertyName("device_class")]
    public string? DeviceClass { get; set; }

    [JsonPropertyName("tailscale_ip")]
    public string? TailscaleIp { get; set; }

    [JsonPropertyName("watcher_node_id")]
    public string? WatcherNodeId { get; set; }

    [JsonPropertyName("capabilities")]
    public Dictionary<string, object?> Capabilities { get; set; } = new();

    [JsonPropertyName("pack_versions")]
    public Dictionary<string, object?> PackVersions { get; set; } = new();

    [JsonPropertyName("policy_profile_version")]
    public string PolicyProfileVersion { get; set; } = "1.0";

    [JsonPropertyName("health")]
    public Dictionary<string, object?> Health { get; set; } = new();

    [JsonPropertyName("cert_fingerprint")]
    public string CertificateFingerprint { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("joined_utc")]
    public DateTimeOffset JoinedUtc { get; set; }

    [JsonPropertyName("last_seen_utc")]
    public DateTimeOffset? LastSeenUtc { get; set; }

    [JsonPropertyName("revoked_utc")]
    public DateTimeOffset? RevokedUtc { get; set; }
}

public sealed class RotationRequest
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("new_cert_fingerprint")]
    public string NewCertificateFingerprint { get; set; } = string.Empty;
}

public sealed class RevokeRequest
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}
