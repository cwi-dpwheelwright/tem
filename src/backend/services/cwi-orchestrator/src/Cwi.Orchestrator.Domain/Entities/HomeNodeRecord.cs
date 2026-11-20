namespace Cwi.Orchestrator.Domain.Entities;

public sealed class HomeNodeRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public string? DeviceClass { get; set; }
    public string? TailscaleIp { get; set; }
    public string? WatcherNodeId { get; set; }
    public string CapabilitiesJson { get; set; } = "{}";
    public string PackVersionsJson { get; set; } = "{}";
    public string PolicyProfileVersion { get; set; } = "1.0";
    public string Status { get; set; } = "registered";
    public DateTimeOffset? LastHeartbeatUtc { get; set; }
    public DateTimeOffset? AssignedUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
