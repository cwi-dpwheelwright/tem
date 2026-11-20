#nullable enable
namespace Cwi.Orchestrator.Application.Models;

public sealed class GraphUpdateCandidateDto
{
    public string CandidateId { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string? UserIdHash { get; init; }
    public string? NodeId { get; init; }
    public string RecursiveEventId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public string CandidateState { get; init; } = string.Empty;
    public string TemporalState { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Relation { get; init; } = string.Empty;
    public string Object { get; init; } = string.Empty;
    public string FactKey { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public string SourceComponent { get; init; } = string.Empty;
    public string SourceHash { get; init; } = string.Empty;
    public DateTimeOffset ObservedUtc { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed class GraphEvolutionCreateResultDto
{
    public bool Created { get; init; }
    public bool Skipped { get; init; }
    public string? Reason { get; init; }
    public GraphUpdateCandidateDto? Candidate { get; init; }
    public GraphVectorRefreshJobDto? VectorRefreshJob { get; init; }
}
#nullable disable
