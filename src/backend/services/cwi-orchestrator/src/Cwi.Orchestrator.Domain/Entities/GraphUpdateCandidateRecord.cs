#nullable enable
namespace Cwi.Orchestrator.Domain.Entities;

public sealed class GraphUpdateCandidateRecord
{
    public Guid Id { get; set; }
    public string CandidateId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? UserIdHash { get; set; }
    public string? NodeId { get; set; }
    public string RecursiveEventId { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
    public string CandidateState { get; set; } = "add";
    public string TemporalState { get; set; } = "current";
    public string Subject { get; set; } = string.Empty;
    public string Relation { get; set; } = string.Empty;
    public string Object { get; set; } = string.Empty;
    public string FactKey { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string ProvenanceJson { get; set; } = "{}";
    public string PayloadJson { get; set; } = "{}";
    public string SourceComponent { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public DateTimeOffset ObservedUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
#nullable disable
