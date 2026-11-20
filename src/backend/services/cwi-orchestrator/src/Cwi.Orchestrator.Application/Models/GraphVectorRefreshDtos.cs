#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class GraphVectorRefreshJobDto
{
    public string JobId { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string? UserIdHash { get; init; }
    public string? NodeId { get; init; }
    public string GraphCandidateId { get; init; } = string.Empty;
    public string RecursiveEventId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public string FactKey { get; init; } = string.Empty;
    public string RefreshReason { get; init; } = string.Empty;
    public string RefreshStatus { get; init; } = string.Empty;
    public string AffectedVectorScope { get; init; } = string.Empty;
    public string SourceHash { get; init; } = string.Empty;
    public int AttemptCount { get; init; }
    public string? LastError { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
}

public sealed class GraphVectorRefreshCreateResultDto
{
    public bool Created { get; init; }
    public bool Skipped { get; init; }
    public string? Reason { get; init; }
    public GraphVectorRefreshJobDto? Job { get; init; }
}

public sealed class GraphVectorRefreshProcessRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string? TenantId { get; init; }

    [JsonPropertyName("page_size")]
    public int PageSize { get; init; } = 25;
}

public sealed class GraphVectorRefreshProcessResultDto
{
    public int Attempted { get; init; }
    public int Completed { get; init; }
    public int Failed { get; init; }
    public IReadOnlyList<GraphVectorRefreshJobDto> Jobs { get; init; } = Array.Empty<GraphVectorRefreshJobDto>();
}

public static class GraphVectorRefreshStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Superseded = "superseded";
}
#nullable disable
