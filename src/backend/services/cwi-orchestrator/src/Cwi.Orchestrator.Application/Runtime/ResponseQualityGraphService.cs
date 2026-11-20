#nullable enable
using System;
using System.Text.Json;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Runtime;

public sealed class ResponseQualityGraphService
{
    private const string QualityObservationEventType = "quality_observation.created";

    private readonly IRecursiveEventRepository _repository;

    public ResponseQualityGraphService(IRecursiveEventRepository repository)
    {
        _repository = repository;
    }

    public async Task<ResponseQualityGraphDto> GetAsync(
        string? tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Min(Math.Max(1, pageSize), 500);
        var records = await _repository.QueryAsync(
            string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim(),
            QualityObservationEventType,
            temporalState: null,
            (normalizedPage - 1) * normalizedPageSize,
            normalizedPageSize,
            cancellationToken).ConfigureAwait(false);

        var issues = records
            .Select(TryMapIssue)
            .Where(issue => issue is not null)
            .Select(issue => issue!)
            .ToArray();

        var active = issues
            .Where(IsActive)
            .OrderByDescending(issue => issue.ObservedUtc)
            .ToArray();
        var resolved = issues
            .Where(issue => !IsActive(issue))
            .OrderByDescending(issue => issue.ObservedUtc)
            .ToArray();

        return new ResponseQualityGraphDto
        {
            ActiveIssues = active,
            ResolvedIssues = resolved,
            RecurringPatterns = BuildPatterns(issues)
        };
    }

    private static IReadOnlyList<ResponseQualityPatternDto> BuildPatterns(
        IReadOnlyCollection<ResponseQualityIssueDto> issues)
    {
        return issues
            .GroupBy(
                issue => BuildPatternKey(issue.Rule, issue.Route, issue.PromptType),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                var activeCount = group.Count(IsActive);
                var resolvedCount = group.Count(issue => !IsActive(issue));
                var scores = group
                    .Where(issue => issue.CriticScore.HasValue)
                    .Select(issue => issue.CriticScore!.Value)
                    .ToArray();
                return new ResponseQualityPatternDto
                {
                    Rule = first.Rule,
                    Route = first.Route,
                    PromptType = first.PromptType,
                    ActiveCount = activeCount,
                    ResolvedCount = resolvedCount,
                    AverageCriticScore = scores.Length == 0
                        ? null
                        : Math.Round(scores.Average(), 3),
                    LastObservedUtc = group.Max(issue => issue.ObservedUtc)
                };
            })
            .Where(pattern => pattern.ActiveCount + pattern.ResolvedCount > 1)
            .OrderByDescending(pattern => pattern.ActiveCount)
            .ThenBy(pattern => pattern.AverageCriticScore ?? 1.0d)
            .ThenByDescending(pattern => pattern.LastObservedUtc)
            .ToArray();
    }

    private static string BuildPatternKey(string rule, string route, string promptType)
    {
        return string.Join('\u001f', rule.Trim(), route.Trim(), promptType.Trim());
    }

    private static ResponseQualityIssueDto? TryMapIssue(RecursiveEventRecord record)
    {
        try
        {
            using var document = JsonDocument.Parse(record.PayloadJson);
            var root = document.RootElement;
            return new ResponseQualityIssueDto
            {
                EventId = record.EventId,
                TenantId = record.TenantId,
                TraceId = record.TraceId,
                Category = GetString(root, "category", "unknown"),
                Rule = GetString(root, "rule", "unknown"),
                Severity = GetString(root, "severity", "unknown"),
                Detail = GetString(root, "detail", string.Empty),
                Route = GetString(root, "route", "unknown"),
                PromptType = GetString(root, "prompt_type", "unknown"),
                SourceBehavior = GetString(root, "source_behavior", "unknown"),
                CriticScore = GetNullableDouble(root, "critic_score"),
                FixStatus = GetString(root, "fix_status", "active"),
                TemporalState = string.IsNullOrWhiteSpace(record.TemporalState)
                    ? "current"
                    : record.TemporalState,
                SourceComponent = record.SourceComponent,
                ObservedUtc = record.TimestampUtc
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsActive(ResponseQualityIssueDto issue)
    {
        return string.Equals(issue.FixStatus, "active", StringComparison.OrdinalIgnoreCase)
               && string.Equals(issue.TemporalState, "current", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetString(JsonElement root, string propertyName, string fallback)
    {
        if (root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString()))
        {
            return value.GetString()!.Trim();
        }

        return fallback;
    }

    private static double? GetNullableDouble(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
#nullable disable
