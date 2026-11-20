namespace Cwi.Orchestrator.Domain.Entities;

public sealed class SuccessionSnapshot
{
    public Guid Id { get; set; }
    public string SnapshotId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string PlanId { get; set; } = string.Empty;
    public string SnapshotJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
}
