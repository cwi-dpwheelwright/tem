#nullable enable
namespace Cwi.Orchestrator.Application.Models;

public sealed class ResponseQualityGraphDto
{
    public IReadOnlyList<ResponseQualityIssueDto> ActiveIssues { get; init; } = Array.Empty<ResponseQualityIssueDto>();
    public IReadOnlyList<ResponseQualityIssueDto> ResolvedIssues { get; init; } = Array.Empty<ResponseQualityIssueDto>();
    public IReadOnlyList<ResponseQualityPatternDto> RecurringPatterns { get; init; } = Array.Empty<ResponseQualityPatternDto>();
}

public sealed class ResponseQualityIssueDto
{
    public string EventId { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Rule { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string Route { get; init; } = string.Empty;
    public string PromptType { get; init; } = string.Empty;
    public string SourceBehavior { get; init; } = string.Empty;
    public double? CriticScore { get; init; }
    public string FixStatus { get; init; } = string.Empty;
    public string TemporalState { get; init; } = string.Empty;
    public string SourceComponent { get; init; } = string.Empty;
    public DateTimeOffset ObservedUtc { get; init; }
}

public sealed class ResponseQualityPatternDto
{
    public string Rule { get; init; } = string.Empty;
    public string Route { get; init; } = string.Empty;
    public string PromptType { get; init; } = string.Empty;
    public int ActiveCount { get; init; }
    public int ResolvedCount { get; init; }
    public double? AverageCriticScore { get; init; }
    public DateTimeOffset LastObservedUtc { get; init; }
}
#nullable disable
