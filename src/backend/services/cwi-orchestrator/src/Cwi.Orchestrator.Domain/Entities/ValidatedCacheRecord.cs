#nullable enable
namespace Cwi.Orchestrator.Domain.Entities;

public sealed class ValidatedCacheRecord
{
    public Guid Id { get; set; }
    public string CacheKey { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Scope { get; set; } = "tenant";
    public string? UserIdHash { get; set; }
    public string SourceProofJson { get; set; } = "{}";
    public string TraceId { get; set; } = string.Empty;
    public DateTimeOffset FreshUntilUtc { get; set; }
    public string PolicyMode { get; set; } = string.Empty;
    public string? PolicyVersion { get; set; }
    public string PackHash { get; set; } = string.Empty;
    public string ValidationStatus { get; set; } = "pending";
    public string? CriticStatus { get; set; }
    public string? PayloadHash { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
#nullable disable
