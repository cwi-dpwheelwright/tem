#nullable enable
using System.Security.Cryptography;
using System.Text;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Graph;

public sealed class UserIntelligenceService
{
    private static readonly (string Name, string Title, string Question)[] DailySummaryQueries =
    {
        ("priorities", "Priorities", "What do I need to do this week?"),
        ("open_loops", "Open loops", "What are my open loops?"),
        ("current_work", "Current work", "What have I been working on?"),
        ("decisions", "Recent decisions", "What decisions have I made?")
    };

    private static readonly (string Name, string Title, string Question)[] WeeklySummaryQueries =
    {
        ("current_work", "Current work", "What have I been working on?"),
        ("goals", "Goals", "What are my goals?"),
        ("open_loops", "Open loops", "What are my open loops?"),
        ("home_life", "Home life", "What do you know about my home life?"),
        ("work_life", "Work life", "What do you know about my work life?")
    };

    private readonly GraphQueryService _graphQueryService;
    private readonly UserGraphProfileService _profileService;

    public UserIntelligenceService(
        GraphQueryService graphQueryService,
        UserGraphProfileService profileService)
    {
        _graphQueryService = graphQueryService;
        _profileService = profileService;
    }

    public Task<ServiceResponse<UserGraphProfileResponseDto>> GetProfileAsync(
        string tenantId,
        string? userId,
        int topK,
        CancellationToken cancellationToken)
    {
        return _profileService.GetAsync(tenantId, userId, topK, cancellationToken);
    }

    public async Task<ServiceResponse<UserIntelligenceSummaryDto>> GetSummaryAsync(
        string tenantId,
        string? userId,
        string period,
        int topK,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ServiceResponse<UserIntelligenceSummaryDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id is required."),
                statusCode: 400,
                message: "tenant_id is required");
        }

        var normalizedPeriod = NormalizePeriod(period);
        if (normalizedPeriod is null)
        {
            return ServiceResponse<UserIntelligenceSummaryDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "period must be daily or weekly."),
                statusCode: 400,
                message: "period must be daily or weekly");
        }

        var queries = normalizedPeriod == "weekly" ? WeeklySummaryQueries : DailySummaryQueries;
        var sections = new List<UserIntelligenceSummarySectionDto>();
        foreach (var (name, title, question) in queries)
        {
            var query = await _graphQueryService.QueryAsync(new GraphQueryRequestDto
            {
                TenantId = tenantId,
                UserId = userId,
                Question = question,
                TopK = Math.Clamp(topK, 1, 20)
            }, cancellationToken).ConfigureAwait(false);

            if (!query.IsSuccess || query.Data is null)
            {
                continue;
            }

            sections.Add(new UserIntelligenceSummarySectionDto
            {
                Name = name,
                Title = title,
                Items = query.Data.Evidence.Select(evidence => new UserIntelligenceSummaryItemDto
                {
                    Text = evidence.Object,
                    Category = evidence.Subject,
                    SourceDocument = evidence.SourceDocument,
                    Inferred = evidence.Inferred
                }).Where(item => !string.IsNullOrWhiteSpace(item.Text)).ToArray()
            });
        }

        return ServiceResponse<UserIntelligenceSummaryDto>.Success(new UserIntelligenceSummaryDto
        {
            TenantId = tenantId.Trim(),
            UserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim(),
            Period = normalizedPeriod,
            GeneratedUtc = DateTimeOffset.UtcNow,
            Sections = sections
        });
    }

    public async Task<ServiceResponse<UserSuggestionListDto>> GetSuggestionsAsync(
        string tenantId,
        string? userId,
        int topK,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ServiceResponse<UserSuggestionListDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id is required."),
                statusCode: 400,
                message: "tenant_id is required");
        }

        var openLoops = await _graphQueryService.QueryAsync(new GraphQueryRequestDto
        {
            TenantId = tenantId,
            UserId = userId,
            Question = "What are my open loops?",
            TopK = Math.Clamp(topK, 1, 20)
        }, cancellationToken).ConfigureAwait(false);
        var goals = await _graphQueryService.QueryAsync(new GraphQueryRequestDto
        {
            TenantId = tenantId,
            UserId = userId,
            Question = "What are my goals?",
            TopK = Math.Clamp(topK, 1, 20)
        }, cancellationToken).ConfigureAwait(false);

        var suggestions = BuildSuggestions(openLoops.Data?.Evidence ?? Array.Empty<GraphEvidenceDto>(), goals.Data?.Evidence ?? Array.Empty<GraphEvidenceDto>());
        return ServiceResponse<UserSuggestionListDto>.Success(new UserSuggestionListDto
        {
            TenantId = tenantId.Trim(),
            UserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim(),
            GeneratedUtc = DateTimeOffset.UtcNow,
            Suggestions = suggestions
        });
    }

    public async Task<ServiceResponse<UserChangeListDto>> GetChangesAsync(
        string tenantId,
        string? userId,
        int topK,
        CancellationToken cancellationToken)
    {
        var validation = ValidateTenant<UserChangeListDto>(tenantId);
        if (validation is not null)
        {
            return validation;
        }

        var evidence = await QueryEvidenceAsync(
            tenantId,
            userId,
            "What changed since yesterday?",
            topK,
            cancellationToken).ConfigureAwait(false);

        return ServiceResponse<UserChangeListDto>.Success(new UserChangeListDto
        {
            TenantId = tenantId.Trim(),
            UserId = NormalizeUserId(userId),
            GeneratedUtc = DateTimeOffset.UtcNow,
            Changes = BuildSignals("recent_change", "New or changed item", evidence, topK)
        });
    }

    public async Task<ServiceResponse<UserRiskListDto>> GetRisksAsync(
        string tenantId,
        string? userId,
        int topK,
        CancellationToken cancellationToken)
    {
        var validation = ValidateTenant<UserRiskListDto>(tenantId);
        if (validation is not null)
        {
            return validation;
        }

        var evidence = await QueryEvidenceAsync(
            tenantId,
            userId,
            "Are there any risks I should know about?",
            topK,
            cancellationToken).ConfigureAwait(false);

        return ServiceResponse<UserRiskListDto>.Success(new UserRiskListDto
        {
            TenantId = tenantId.Trim(),
            UserId = NormalizeUserId(userId),
            GeneratedUtc = DateTimeOffset.UtcNow,
            Risks = BuildSignals("risk", "Potential risk", evidence, topK)
        });
    }

    public async Task<ServiceResponse<UserPlanDto>> GetPlanAsync(
        string tenantId,
        string? userId,
        int topK,
        CancellationToken cancellationToken)
    {
        var validation = ValidateTenant<UserPlanDto>(tenantId);
        if (validation is not null)
        {
            return validation;
        }

        var evidence = await QueryEvidenceAsync(
            tenantId,
            userId,
            "What should I focus on first?",
            topK,
            cancellationToken).ConfigureAwait(false);

        return ServiceResponse<UserPlanDto>.Success(new UserPlanDto
        {
            TenantId = tenantId.Trim(),
            UserId = NormalizeUserId(userId),
            GeneratedUtc = DateTimeOffset.UtcNow,
            Items = BuildPlanItems(evidence, topK)
        });
    }

    public async Task<ServiceResponse<UserBriefingDto>> GetBriefingAsync(
        string tenantId,
        string? userId,
        int topK,
        CancellationToken cancellationToken)
    {
        var validation = ValidateTenant<UserBriefingDto>(tenantId);
        if (validation is not null)
        {
            return validation;
        }

        var summary = await GetSummaryAsync(tenantId, userId, "daily", topK, cancellationToken).ConfigureAwait(false);
        var changes = await GetChangesAsync(tenantId, userId, topK, cancellationToken).ConfigureAwait(false);
        var risks = await GetRisksAsync(tenantId, userId, topK, cancellationToken).ConfigureAwait(false);
        var suggestions = await GetSuggestionsAsync(tenantId, userId, topK, cancellationToken).ConfigureAwait(false);
        var plan = await GetPlanAsync(tenantId, userId, topK, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<UserBriefingDto>.Success(new UserBriefingDto
        {
            TenantId = tenantId.Trim(),
            UserId = NormalizeUserId(userId),
            GeneratedUtc = DateTimeOffset.UtcNow,
            DailySummary = summary.Data,
            Changes = changes.Data?.Changes ?? Array.Empty<UserIntelligenceSignalDto>(),
            Risks = risks.Data?.Risks ?? Array.Empty<UserIntelligenceSignalDto>(),
            Suggestions = suggestions.Data?.Suggestions ?? Array.Empty<UserSuggestionDto>(),
            Plan = plan.Data?.Items ?? Array.Empty<UserPlanItemDto>()
        });
    }

    private static IReadOnlyList<UserSuggestionDto> BuildSuggestions(
        IReadOnlyList<GraphEvidenceDto> openLoops,
        IReadOnlyList<GraphEvidenceDto> goals)
    {
        var suggestions = new List<UserSuggestionDto>();
        foreach (var evidence in openLoops.Where(item => !string.IsNullOrWhiteSpace(item.Object)).Take(8))
        {
            suggestions.Add(new UserSuggestionDto
            {
                SuggestionId = StableId("open_loop", evidence.Object, evidence.SourceDocument),
                Type = "next_action",
                Priority = ResolvePriority(evidence.Object),
                Title = "Follow up on open loop",
                Rationale = evidence.Object,
                SourceDocument = evidence.SourceDocument,
                Confidence = ResolveConfidence(evidence)
            });
        }

        foreach (var evidence in goals.Where(item => !string.IsNullOrWhiteSpace(item.Object)).Take(Math.Max(0, 8 - suggestions.Count)))
        {
            suggestions.Add(new UserSuggestionDto
            {
                SuggestionId = StableId("goal", evidence.Object, evidence.SourceDocument),
                Type = "goal_review",
                Priority = "normal",
                Title = "Review progress toward goal",
                Rationale = evidence.Object,
                SourceDocument = evidence.SourceDocument,
                Confidence = ResolveConfidence(evidence)
            });
        }

        return suggestions;
    }

    private async Task<IReadOnlyList<GraphEvidenceDto>> QueryEvidenceAsync(
        string tenantId,
        string? userId,
        string question,
        int topK,
        CancellationToken cancellationToken)
    {
        var result = await _graphQueryService.QueryAsync(new GraphQueryRequestDto
        {
            TenantId = tenantId,
            UserId = userId,
            Question = question,
            TopK = Math.Clamp(topK, 1, 20)
        }, cancellationToken).ConfigureAwait(false);

        return result.Data?.Evidence ?? Array.Empty<GraphEvidenceDto>();
    }

    private static IReadOnlyList<UserIntelligenceSignalDto> BuildSignals(
        string type,
        string title,
        IReadOnlyList<GraphEvidenceDto> evidence,
        int topK)
    {
        return evidence
            .Where(item => !string.IsNullOrWhiteSpace(item.Object))
            .Select(item =>
            {
                var score = ResolvePriorityScore(item.Object, item.Subject);
                return new UserIntelligenceSignalDto
                {
                    SignalId = StableId(type, item.Object, item.SourceDocument),
                    Type = type,
                    Priority = ResolvePriority(score),
                    PriorityScore = score,
                    Title = title,
                    Detail = item.Object,
                    Category = item.Subject,
                    SourceDocument = item.SourceDocument,
                    Inferred = item.Inferred,
                    Confidence = ResolveConfidence(item)
                };
            })
            .OrderByDescending(item => item.PriorityScore)
            .ThenBy(item => item.Detail, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(topK, 1, 20))
            .ToArray();
    }

    private static IReadOnlyList<UserPlanItemDto> BuildPlanItems(
        IReadOnlyList<GraphEvidenceDto> evidence,
        int topK)
    {
        var ranked = evidence
            .Where(item => !string.IsNullOrWhiteSpace(item.Object))
            .Select(item =>
            {
                var score = ResolvePriorityScore(item.Object, item.Subject);
                return new
                {
                    Evidence = item,
                    Score = score
                };
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Evidence.Object, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(topK, 1, 20))
            .ToArray();

        return ranked.Select((item, index) => new UserPlanItemDto
        {
            Rank = index + 1,
            PriorityScore = item.Score,
            Priority = ResolvePriority(item.Score),
            Title = BuildPlanTitle(item.Evidence.Object),
            Rationale = item.Evidence.Object,
            Category = item.Evidence.Subject,
            SourceDocument = item.Evidence.SourceDocument,
            Confidence = ResolveConfidence(item.Evidence)
        }).ToArray();
    }

    private static string? NormalizePeriod(string period)
    {
        var normalized = period.Trim().ToLowerInvariant();
        return normalized is "daily" or "weekly" ? normalized : null;
    }

    private static string ResolvePriority(string text)
    {
        return ResolvePriority(ResolvePriorityScore(text, null));
    }

    private static string ResolvePriority(double score)
    {
        if (score >= 0.80)
        {
            return "high";
        }

        return score >= 0.55 ? "normal" : "low";
    }

    private static double ResolvePriorityScore(string text, string? category)
    {
        var lower = text.ToLowerInvariant();
        var score = 0.35;
        if (lower.Contains("deadline", StringComparison.Ordinal)
            || lower.Contains("due", StringComparison.Ordinal)
            || lower.Contains("today", StringComparison.Ordinal)
            || lower.Contains("tomorrow", StringComparison.Ordinal)
            || lower.Contains("this week", StringComparison.Ordinal))
        {
            score += 0.45;
        }

        if (lower.Contains("blocked", StringComparison.Ordinal)
            || lower.Contains("risk", StringComparison.Ordinal)
            || lower.Contains("urgent", StringComparison.Ordinal)
            || lower.Contains("missing", StringComparison.Ordinal))
        {
            score += 0.25;
        }

        if (lower.Contains("bill", StringComparison.Ordinal)
            || lower.Contains("payment", StringComparison.Ordinal)
            || lower.Contains("lab", StringComparison.Ordinal)
            || lower.Contains("appointment", StringComparison.Ordinal))
        {
            score += 0.15;
        }

        if (!string.IsNullOrWhiteSpace(category)
            && (category.Contains("issue", StringComparison.OrdinalIgnoreCase)
                || category.Contains("task", StringComparison.OrdinalIgnoreCase)
                || category.Contains("open", StringComparison.OrdinalIgnoreCase)))
        {
            score += 0.10;
        }

        return Math.Round(Math.Min(score, 0.99), 2);
    }

    private static string BuildPlanTitle(string detail)
    {
        var text = detail.Trim();
        if (text.Length <= 80)
        {
            return text;
        }

        return text[..77].TrimEnd() + "...";
    }

    private static double ResolveConfidence(GraphEvidenceDto evidence)
    {
        return evidence.Inferred ? 0.65 : 0.80;
    }

    private static string? NormalizeUserId(string? userId)
    {
        return string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
    }

    private static ServiceResponse<T>? ValidateTenant<T>(string tenantId)
    {
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            return null;
        }

        return ServiceResponse<T>.Fail(
            new DomainError(ErrorCodes.InvalidRequest, "tenant_id is required."),
            statusCode: 400,
            message: "tenant_id is required");
    }

    private static string StableId(params string?[] parts)
    {
        var joined = string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return "sug_" + Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }
}
#nullable disable
