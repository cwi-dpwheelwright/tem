namespace Cwi.Coordination.Domain.Entities;

public enum MembershipStatus
{
    Pending,
    Active,
    Revoked
}

public sealed class Member
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string NodeId { get; init; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public string? DeviceClass { get; set; }
    public string? TailscaleIp { get; set; }
    public string? WatcherNodeId { get; set; }
    public string CapabilitiesJson { get; set; } = "{}";
    public string PackVersionsJson { get; set; } = "{}";
    public string PolicyProfileVersion { get; set; } = "1.0";
    public string HealthJson { get; set; } = "{}";
    public string CertificateFingerprint { get; set; } = string.Empty;
    public MembershipStatus Status { get; set; } = MembershipStatus.Pending;
    public DateTimeOffset JoinedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenUtc { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
}
