#nullable enable
namespace Cwi.Orchestrator.Domain.Entities;

public sealed class NormalizedConnectorRecordEntity
{
    public Guid Id { get; set; }
    public string RecordId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string ConnectorType { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public string SourceRecordId { get; set; } = string.Empty;
    public string? SourceThreadId { get; set; }
    public string? SourceUrl { get; set; }
    public DateTimeOffset EventTime { get; set; }
    public DateTimeOffset IngestedAt { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? RawExcerpt { get; set; }
    public string LifeWorkScope { get; set; } = string.Empty;
    public string SensitivityLabel { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string SourceSha256 { get; set; } = string.Empty;
    public string RecordJson { get; set; } = "{}";
    public string ProvenanceJson { get; set; } = "{}";
    public string SyncStateJson { get; set; } = "{}";
    public bool IsDeleted { get; set; }
    public bool IsUpdate { get; set; }
    public bool Stale { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public int ReplayCount { get; set; }
}
#nullable disable
