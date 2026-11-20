#nullable enable
namespace Cwi.Orchestrator.Domain.Entities;

public sealed class MemoryFactIndexRecord
{
    public Guid Id { get; set; }
    public string FactKey { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string LifeWorkScope { get; set; } = string.Empty;
    public string MemoryDomain { get; set; } = string.Empty;
    public string MemoryType { get; set; } = string.Empty;
    public string NormalizedFactText { get; set; } = string.Empty;
    public string RecordIdsJson { get; set; } = "[]";
    public double Confidence { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}
#nullable disable
