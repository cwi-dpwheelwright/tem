#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Workflows;

namespace Cwi.Orchestrator.Application.Memory;

public sealed class MemoryFrameworkService
{
    public const string Version = "memory-framework-v1";
    private static readonly Regex SupersedesRegex = new(
        @"\b(?:supersedes|replaces|updates|superseded_memory_id)\s*[:=]\s*(?<id>[A-Za-z0-9_.:-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExplicitCurrentProjectRegex = new(
        @"\b(?:current|active)\b.{0,80}\b(?:project|priority|workstream)\b|\b(?:project|priority|workstream)\b.{0,80}\b(?:current|active)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FirstPersonActiveWorkRegex = new(
        @"\b(?:i|my|we|our)\b.{0,80}\b(?:working\s+on|building|implementing|fixing|own|owns|responsible|priority|priorities|open\s+loop|next\s+action)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WorkTrackingMarkerRegex = new(
        @"\b(?:OPEN|TODO|TASK|BUG|FIX|BLOCKED)-[A-Z0-9][A-Z0-9-]{2,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public MemoryFrameworkResult BuildActiveContext(
        AskWorkflowContext context,
        IReadOnlyList<GovernedMemoryResult> candidates,
        int limit)
    {
        if (candidates.Count == 0)
        {
            return new MemoryFrameworkResult { FrameworkVersion = Version };
        }

        var decisions = new List<MemoryFrameworkDecision>();
        var working = new List<MemoryCandidate>();
        foreach (var memory in candidates)
        {
            var slot = ResolveSlot(memory);
            var temporalState = ResolveTemporalState(memory);
            var exclusionReason = ResolveExclusionReason(context, memory, temporalState);
            if (exclusionReason is not null)
            {
                decisions.Add(Decision(memory, slot, "excluded", exclusionReason, temporalState));
                continue;
            }

            working.Add(new MemoryCandidate(memory, slot, temporalState, BuildDuplicateKey(memory, slot)));
        }

        var supersededIds = working
            .SelectMany(candidate => ExtractSupersededMemoryIds(candidate.Memory))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (supersededIds.Count > 0)
        {
            var retained = new List<MemoryCandidate>();
            foreach (var candidate in working)
            {
                if (supersededIds.Contains(candidate.Memory.MemoryId))
                {
                    decisions.Add(Decision(
                        candidate.Memory,
                        candidate.Slot,
                        "excluded",
                        "superseded_by_newer_memory",
                        "superseded"));
                    continue;
                }

                retained.Add(candidate);
            }

            working = retained;
        }

        var conflicts = BuildConflictSummaries(working);
        if (conflicts.Count > 0)
        {
            var conflictedIds = conflicts
                .SelectMany(conflict => conflict.MemoryIds)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            working = working
                .Where(candidate =>
                {
                    if (!conflictedIds.Contains(candidate.Memory.MemoryId))
                    {
                        return true;
                    }

                    decisions.Add(Decision(
                        candidate.Memory,
                        candidate.Slot,
                        "excluded",
                        "unresolved_conflict",
                        candidate.TemporalState));
                    return false;
                })
                .ToList();
        }

        var active = new List<GovernedMemoryResult>();
        foreach (var group in working.GroupBy(candidate => candidate.DuplicateKey, StringComparer.OrdinalIgnoreCase))
        {
            var selected = group
                .OrderByDescending(candidate => candidate.Memory.RelevanceScore)
                .ThenByDescending(candidate => candidate.Memory.Confidence)
                .ThenBy(candidate => candidate.Memory.MemoryId, StringComparer.OrdinalIgnoreCase)
                .First();
            active.Add(selected.Memory);
            decisions.Add(Decision(
                selected.Memory,
                selected.Slot,
                "active",
                "selected_for_active_context",
                selected.TemporalState));

            foreach (var duplicate in group.Where(candidate => !ReferenceEquals(candidate, selected)))
            {
                decisions.Add(Decision(
                    duplicate.Memory,
                    duplicate.Slot,
                    "excluded",
                    "duplicate_suppressed",
                    duplicate.TemporalState));
            }
        }

        var max = Math.Max(1, limit);
        return new MemoryFrameworkResult
        {
            FrameworkVersion = Version,
            ActiveMemories = active
                .OrderByDescending(memory => memory.RelevanceScore)
                .ThenByDescending(memory => memory.Confidence)
                .ThenBy(memory => memory.MemoryId, StringComparer.OrdinalIgnoreCase)
                .Take(max)
                .ToArray(),
            Decisions = decisions.ToArray(),
            Conflicts = conflicts.ToArray()
        };
    }

    private static string? ResolveExclusionReason(
        AskWorkflowContext context,
        GovernedMemoryResult memory,
        string temporalState)
    {
        if (!IsAllowedScope(context, memory))
        {
            return "scope_not_permitted";
        }

        var minConfidence = IsStrictActiveContext(context) ? 0.70f : 0.60f;
        var minRelevance = IsStrictActiveContext(context) ? 0.70f : 0.35f;
        if (memory.Confidence < minConfidence)
        {
            return "confidence_below_active_context_threshold";
        }

        if (memory.RelevanceScore < minRelevance)
        {
            return "relevance_below_active_context_threshold";
        }

        if (temporalState is "deleted" or "retracted" or "expired" or "superseded")
        {
            return $"temporal_state_{temporalState}";
        }

        if (temporalState == "historical" && !IsHistoricalQuery(context))
        {
            return "historical_memory_not_active_for_current_query";
        }

        if (IsCurrentWorkQuery(context)
            && IsUploadedDocumentMemory(memory)
            && !HasConfirmedActiveWorkEvidence(memory))
        {
            return "uploaded_document_not_confirmed_active_work";
        }

        return null;
    }

    private static bool IsStrictActiveContext(AskWorkflowContext context)
    {
        return string.Equals(context.QueryClassification, "memory_graph_request", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.IntentType, "memory_recall", StringComparison.OrdinalIgnoreCase)
               || AskUploadedContextLookupPolicy.IsUploadedContextLookup(context)
               || (context.RetrievalDomain?.Contains("memory_graph", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool IsAllowedScope(AskWorkflowContext context, GovernedMemoryResult memory)
    {
        if (!string.Equals(memory.TenantId, context.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(memory.UserId))
        {
            return string.Equals(memory.UserId.Trim(), context.UserId, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(memory.MemoryScope, "tenant", StringComparison.OrdinalIgnoreCase)
               || memory.SourceType.Contains("tenant", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<MemoryConflictSummary> BuildConflictSummaries(IReadOnlyList<MemoryCandidate> candidates)
    {
        var conflictCandidates = candidates
            .Where(candidate => HasConflictMarker(candidate.Memory))
            .GroupBy(candidate => candidate.Slot, StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<MemoryConflictSummary>();
        foreach (var group in conflictCandidates)
        {
            var memories = group.Select(candidate => candidate.Memory).ToArray();
            if (memories.Length == 0)
            {
                continue;
            }

            conflicts.Add(new MemoryConflictSummary
            {
                ConflictId = $"conflict_{StableHash(group.Key, string.Join("|", memories.Select(memory => memory.MemoryId)))[..16]}",
                Slot = group.Key,
                Summary = $"Unresolved memory conflict in {group.Key}: {Compact(memories[0].Text, 180)}",
                MemoryIds = memories.Select(memory => memory.MemoryId).ToArray()
            });
        }

        return conflicts;
    }

    private static bool HasConflictMarker(GovernedMemoryResult memory)
    {
        var haystack = $"{memory.MemoryScope} {memory.SourceType} {memory.Text}";
        return ContainsAny(haystack, "conflict:", "conflicted", "contradicts", "contradiction", "disagrees", "ambiguous", "no single truth");
    }

    private static IEnumerable<string> ExtractSupersededMemoryIds(GovernedMemoryResult memory)
    {
        var haystack = $"{memory.SourceType} {memory.Text}";
        foreach (Match match in SupersedesRegex.Matches(haystack))
        {
            var id = match.Groups["id"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(id))
            {
                yield return id;
            }
        }
    }

    private static string ResolveTemporalState(GovernedMemoryResult memory)
    {
        var haystack = $"{memory.MemoryScope} {memory.SourceType} {memory.Text}";
        if (ContainsAny(haystack, "deleted_memory", "deleted:", "forget this", "remove this memory"))
        {
            return "deleted";
        }

        if (ContainsAny(haystack, "retracted_memory", "retracted:", "retraction:"))
        {
            return "retracted";
        }

        if (ContainsAny(haystack, "expired_memory", "expired:", "no longer valid"))
        {
            return "expired";
        }

        if (ContainsAny(haystack, "superseded_memory", "superseded:"))
        {
            return "superseded";
        }

        if (ContainsAny(haystack, "historical_memory", "historical:", "formerly", "previously"))
        {
            return "historical";
        }

        return "current";
    }

    private static bool IsHistoricalQuery(AskWorkflowContext context)
    {
        var question = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion);
        return ContainsAny(question, "history", "historical", "previous", "previously", "used to", "what changed", "changed");
    }

    private static bool IsCurrentWorkQuery(AskWorkflowContext context)
    {
        var question = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion);
        var retrievalDomain = context.RetrievalDomain ?? string.Empty;
        return retrievalDomain.Contains("work_memory_graph", StringComparison.OrdinalIgnoreCase)
               || retrievalDomain.Contains("active_context_memory_graph", StringComparison.OrdinalIgnoreCase)
               || ContainsAny(
                   question,
                   "what am i working on",
                   "what have i been working on",
                   "what projects am i working on",
                   "what projects am i currently working on",
                   "current priorities",
                   "current cwi priorities",
                   "latest cwi project context",
                   "true right now",
                   "active now");
    }

    private static bool IsUploadedDocumentMemory(GovernedMemoryResult memory)
    {
        return memory.MemoryId.StartsWith("mem-upload-", StringComparison.OrdinalIgnoreCase)
               || memory.SourceType.Contains("uploaded_context", StringComparison.OrdinalIgnoreCase)
               || memory.SourceType.Contains("context_upload", StringComparison.OrdinalIgnoreCase)
               || memory.SourceType.Contains("uploaded_source", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasConfirmedActiveWorkEvidence(GovernedMemoryResult memory)
    {
        var haystack = $"{memory.SourceType} {memory.Text}";
        return FirstPersonActiveWorkRegex.IsMatch(haystack)
               || ExplicitCurrentProjectRegex.IsMatch(haystack)
               || WorkTrackingMarkerRegex.IsMatch(haystack);
    }

    private static string ResolveSlot(GovernedMemoryResult memory)
    {
        var haystack = $"{memory.MemoryScope} {memory.SourceType} {memory.Text}";
        if (ContainsAny(haystack, "relationship", "person", "people", "collaborator", "network", "owns"))
        {
            return "relationships";
        }

        if (ContainsAny(haystack, "open_loop", "open loop", "pending", "next action", "todo", "to do", "blocked"))
        {
            return "open_loops";
        }

        if (ContainsAny(haystack, "preference", "prefers", "preferred", "likes"))
        {
            return "preferences";
        }

        if (ContainsAny(haystack, "identity", "profile", "role"))
        {
            return "identity";
        }

        if (ContainsAny(haystack, "project", "work", "career", "mcp", "cwi", "orchestrator", "grafana", "smartsheet"))
        {
            return "projects";
        }

        if (ContainsAny(haystack, "home", "personal", "family", "health", "wellness", "finance", "pet"))
        {
            return "personal";
        }

        return "general";
    }

    private static string BuildDuplicateKey(GovernedMemoryResult memory, string slot)
    {
        var hash = string.IsNullOrWhiteSpace(memory.ContentSha256)
            ? StableHash(Normalize(memory.Text))
            : memory.ContentSha256.Trim();
        return $"{memory.TenantId}:{memory.UserId ?? memory.MemoryScope}:{slot}:{hash}".ToLowerInvariant();
    }

    private static MemoryFrameworkDecision Decision(
        GovernedMemoryResult memory,
        string slot,
        string outcome,
        string reason,
        string temporalState)
    {
        return new MemoryFrameworkDecision
        {
            MemoryId = memory.MemoryId,
            Slot = slot,
            Outcome = outcome,
            Reason = reason,
            TemporalState = temporalState
        };
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string StableHash(params string?[] parts)
    {
        var joined = string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Compact(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var compact = string.Join(
            " ",
            text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= maxLength ? compact : compact[..maxLength] + "...";
    }

    private sealed record MemoryCandidate(
        GovernedMemoryResult Memory,
        string Slot,
        string TemporalState,
        string DuplicateKey);
}
#nullable disable
