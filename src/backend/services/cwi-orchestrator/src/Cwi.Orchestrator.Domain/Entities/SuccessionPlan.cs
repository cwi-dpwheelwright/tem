namespace Cwi.Orchestrator.Domain.Entities;

public sealed class SuccessionPlan
{
    public Guid Id { get; set; }
    public string PlanId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string AssetId { get; set; } = string.Empty;
    public string CurrentOwnerId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string SuccessorsJson { get; set; } = string.Empty;
    public DateTimeOffset? EffectiveUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
