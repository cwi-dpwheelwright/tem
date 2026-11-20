namespace Cwi.Orchestrator.Domain.Entities;

public sealed class ArtifactRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string ArtifactId { get; set; } = string.Empty;
    public string ArtifactType { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string Status { get; set; } = string.Empty;
    public string VersionsJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
