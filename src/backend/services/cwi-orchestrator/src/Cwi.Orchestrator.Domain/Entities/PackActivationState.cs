namespace Cwi.Orchestrator.Domain.Entities;

public sealed class PackActivationState
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string PackName { get; set; } = string.Empty;
    public string PackHash { get; set; } = string.Empty;
    public string Status { get; set; } = PackActivationStatus.Active;
    public string ActivatedBy { get; set; } = string.Empty;
    public string RolledBackBy { get; set; } = string.Empty;
    public string RollbackReason { get; set; } = string.Empty;
    public DateTimeOffset ActivatedUtc { get; set; }
    public DateTimeOffset? RolledBackUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public static class PackActivationStatus
{
    public const string Active = "active";
    public const string RolledBack = "rolled_back";
}
