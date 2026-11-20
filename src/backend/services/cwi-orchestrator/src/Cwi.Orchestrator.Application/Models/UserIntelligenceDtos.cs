#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class UserIntelligenceSummaryDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("period")]
    public string Period { get; init; } = string.Empty;

    [JsonPropertyName("generated_utc")]
    public DateTimeOffset GeneratedUtc { get; init; }

    [JsonPropertyName("sections")]
    public IReadOnlyList<UserIntelligenceSummarySectionDto> Sections { get; init; } = Array.Empty<UserIntelligenceSummarySectionDto>();
}

public sealed class UserIntelligenceSummarySectionDto
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("items")]
    public IReadOnlyList<UserIntelligenceSummaryItemDto> Items { get; init; } = Array.Empty<UserIntelligenceSummaryItemDto>();
}

public sealed class UserIntelligenceSummaryItemDto
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("source_document")]
    public string? SourceDocument { get; init; }

    [JsonPropertyName("inferred")]
    public bool Inferred { get; init; }
}

public sealed class UserSuggestionListDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("generated_utc")]
    public DateTimeOffset GeneratedUtc { get; init; }

    [JsonPropertyName("suggestions")]
    public IReadOnlyList<UserSuggestionDto> Suggestions { get; init; } = Array.Empty<UserSuggestionDto>();
}

public sealed class UserSuggestionDto
{
    [JsonPropertyName("suggestion_id")]
    public string SuggestionId { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("priority")]
    public string Priority { get; init; } = "normal";

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("rationale")]
    public string Rationale { get; init; } = string.Empty;

    [JsonPropertyName("source_document")]
    public string? SourceDocument { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; } = 0.75;
}

public sealed class UserChangeListDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("generated_utc")]
    public DateTimeOffset GeneratedUtc { get; init; }

    [JsonPropertyName("changes")]
    public IReadOnlyList<UserIntelligenceSignalDto> Changes { get; init; } = Array.Empty<UserIntelligenceSignalDto>();
}

public sealed class UserRiskListDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("generated_utc")]
    public DateTimeOffset GeneratedUtc { get; init; }

    [JsonPropertyName("risks")]
    public IReadOnlyList<UserIntelligenceSignalDto> Risks { get; init; } = Array.Empty<UserIntelligenceSignalDto>();
}

public sealed class UserPlanDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("generated_utc")]
    public DateTimeOffset GeneratedUtc { get; init; }

    [JsonPropertyName("items")]
    public IReadOnlyList<UserPlanItemDto> Items { get; init; } = Array.Empty<UserPlanItemDto>();
}

public sealed class UserPlanItemDto
{
    [JsonPropertyName("rank")]
    public int Rank { get; init; }

    [JsonPropertyName("priority_score")]
    public double PriorityScore { get; init; }

    [JsonPropertyName("priority")]
    public string Priority { get; init; } = "normal";

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("rationale")]
    public string Rationale { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("source_document")]
    public string? SourceDocument { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; } = 0.75;
}

public sealed class UserIntelligenceSignalDto
{
    [JsonPropertyName("signal_id")]
    public string SignalId { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("priority")]
    public string Priority { get; init; } = "normal";

    [JsonPropertyName("priority_score")]
    public double PriorityScore { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("detail")]
    public string Detail { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("source_document")]
    public string? SourceDocument { get; init; }

    [JsonPropertyName("inferred")]
    public bool Inferred { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; } = 0.75;
}

public sealed class UserBriefingDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("generated_utc")]
    public DateTimeOffset GeneratedUtc { get; init; }

    [JsonPropertyName("daily_summary")]
    public UserIntelligenceSummaryDto? DailySummary { get; init; }

    [JsonPropertyName("changes")]
    public IReadOnlyList<UserIntelligenceSignalDto> Changes { get; init; } = Array.Empty<UserIntelligenceSignalDto>();

    [JsonPropertyName("risks")]
    public IReadOnlyList<UserIntelligenceSignalDto> Risks { get; init; } = Array.Empty<UserIntelligenceSignalDto>();

    [JsonPropertyName("suggestions")]
    public IReadOnlyList<UserSuggestionDto> Suggestions { get; init; } = Array.Empty<UserSuggestionDto>();

    [JsonPropertyName("plan")]
    public IReadOnlyList<UserPlanItemDto> Plan { get; init; } = Array.Empty<UserPlanItemDto>();
}
#nullable disable
