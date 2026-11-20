#nullable enable
namespace Cwi.Orchestrator.Domain.Entities;

public sealed class GovernedMemoryRecord
{
    public Guid Id { get; set; }
    public string MemoryId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? NodeId { get; set; }
    public string MemoryScope { get; set; } = "user";
    public string MemoryType { get; set; } = "preference";
    public string MemoryText { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public string SensitivityLevel { get; set; } = "normal";
    public string RetentionPolicy { get; set; } = "durable";
    public string AccessScope { get; set; } = "user";
    public bool LocalOnly { get; set; }
    public bool SyncAllowed { get; set; } = true;
    public string ContentSha256 { get; set; } = string.Empty;
    public string? ProvenanceId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
#nullable disable
