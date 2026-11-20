#nullable enable
namespace Cwi.Orchestrator.Domain.Entities;

public sealed class GraphVectorRefreshJobRecord
{
    public Guid Id { get; set; }
    public string JobId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? UserIdHash { get; set; }
    public string? NodeId { get; set; }
    public string GraphCandidateId { get; set; } = string.Empty;
    public string RecursiveEventId { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
    public string FactKey { get; set; } = string.Empty;
    public string RefreshReason { get; set; } = string.Empty;
    public string RefreshStatus { get; set; } = "pending";
    public string AffectedVectorScope { get; set; } = "graph_fact";
    public string SourceHash { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
}
#nullable disable
