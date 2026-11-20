namespace Cwi.Orchestrator.Domain.Entities;

public sealed class HomeNodeAssignment
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string PrimaryHomeNodeId { get; set; } = string.Empty;
    public string? WatcherNodeId { get; set; }
    public string? PolicyScopeId { get; set; }
    public bool LocalOnly { get; set; } = true;
    public bool RemoteFallbackAllowed { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset AssignedUtc { get; set; }
    public DateTimeOffset? RevokedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
