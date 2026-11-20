namespace Cwi.Orchestrator.Domain.Entities;

public sealed class OwnershipLineageBinding
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string AssetId { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string? ParentAssetId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
