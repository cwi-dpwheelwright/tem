#nullable enable
namespace Cwi.Orchestrator.Domain.Entities;

public sealed class RecursiveEventRecord
{
    public Guid Id { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string SchemaVersion { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ScopeType { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? UserIdHash { get; set; }
    public string? NodeId { get; set; }
    public string SourceComponent { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string? SourceId { get; set; }
    public string? SourceNodeId { get; set; }
    public double Confidence { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string PolicyStatus { get; set; } = string.Empty;
    public string TemporalState { get; set; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string ProvenanceJson { get; set; } = "{}";
    public string EventJson { get; set; } = "{}";
    public string SourceHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
}
#nullable disable
