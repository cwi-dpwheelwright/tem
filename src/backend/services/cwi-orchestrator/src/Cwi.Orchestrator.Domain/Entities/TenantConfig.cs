namespace Cwi.Orchestrator.Domain.Entities;

public sealed class TenantConfig
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
