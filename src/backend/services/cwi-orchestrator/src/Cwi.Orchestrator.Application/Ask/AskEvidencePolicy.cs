#nullable enable
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Retrieval;
using Cwi.Orchestrator.Application.Workflows;

namespace Cwi.Orchestrator.Application.Ask;

public static class AskEvidencePolicy
{
    public static bool ShouldUseRetrievedEvidence(AskWorkflowContext context)
    {
        return context.RetrievalAllowed
               && string.Equals(context.GroundingMode, "source_grounded", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<RetrievalResult> FilterRetrievalResults(
        AskWorkflowContext context,
        IReadOnlyList<RetrievalResult> results)
    {
        if (!ShouldUseRetrievedEvidence(context) || results.Count == 0)
        {
            return Array.Empty<RetrievalResult>();
        }

        var domainTerms = context.RetrievalDomainTerms;
        if (domainTerms.Count == 0)
        {
            return Array.Empty<RetrievalResult>();
        }

        var minScore = context.RetrievalMinScore <= 0 ? 0.35f : context.RetrievalMinScore;
        return results
            .Where(result => result.Score >= minScore)
            .Where(result => MatchesDomain(result, domainTerms))
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.DocId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.ChunkId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<GraphEvidence> FilterGraphEvidence(
        AskWorkflowContext context,
        IReadOnlyList<GraphEvidence> evidence)
    {
        if (!ShouldUseRetrievedEvidence(context) || evidence.Count == 0)
        {
            return Array.Empty<GraphEvidence>();
        }

        var domainTerms = context.RetrievalDomainTerms;
        if (domainTerms.Count == 0)
        {
            return Array.Empty<GraphEvidence>();
        }

        return evidence
            .Where(item => MatchesDomain(item, domainTerms))
            .ToList();
    }

    public static IReadOnlyList<GovernedSourceResult> FilterSourceResults(
        AskWorkflowContext context,
        IReadOnlyList<GovernedSourceResult> results)
    {
        if (!ShouldUseRetrievedEvidence(context) || results.Count == 0)
        {
            return Array.Empty<GovernedSourceResult>();
        }

        var domainTerms = context.RetrievalDomainTerms;
        if (domainTerms.Count == 0)
        {
            return Array.Empty<GovernedSourceResult>();
        }

        var minScore = context.RetrievalMinScore <= 0 ? 0.35f : context.RetrievalMinScore;
        return results
            .Where(result => result.RelevanceScore >= minScore)
            .Where(result => MatchesDomain(result, domainTerms))
            .OrderByDescending(result => result.RelevanceScore)
            .ThenBy(result => result.SourceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.ChunkId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<GovernedMemoryResult> FilterMemoryResults(
        AskWorkflowContext context,
        IReadOnlyList<GovernedMemoryResult> results)
    {
        if (!ShouldUseRetrievedEvidence(context) || results.Count == 0)
        {
            return Array.Empty<GovernedMemoryResult>();
        }

        var domainTerms = context.RetrievalDomainTerms;
        if (domainTerms.Count == 0)
        {
            return Array.Empty<GovernedMemoryResult>();
        }

        var minScore = Math.Max(0.70f, context.RetrievalMinScore <= 0 ? 0.35f : context.RetrievalMinScore);
        return results
            .Where(result => result.RelevanceScore >= minScore && result.Confidence >= 0.70f)
            .Where(result => MatchesDomain(result, domainTerms))
            .OrderByDescending(result => result.RelevanceScore)
            .ThenByDescending(result => result.Confidence)
            .ThenBy(result => result.MemoryId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool HasGroundedEvidence(AskWorkflowContext context)
    {
        if (context.QueryClassification is "identity_definition" or "home_node_definition" or "system_truth")
        {
            return context.BaselinePackGuidance.Any(pack =>
                pack.HasRole("system_truth")
                || string.Equals(pack.PackName, "cwi-identity", StringComparison.OrdinalIgnoreCase));
        }

        if (!ShouldUseRetrievedEvidence(context))
        {
            return false;
        }

        var hasSourceEvidence = context.GraphEvidence.Count > 0
            || context.Retrieved.Count > 0
            || context.SourceResults.Count > 0
            || context.HomeNodeSources.Count > 0;
        var hasGroundingMemory = context.MemoryResults.Any(memory => memory.RelevanceScore >= 0.80f && memory.Confidence >= 0.70f);
        if (context.EvidenceRequired)
        {
            return hasSourceEvidence || (IsActivatedMemoryRecall(context) && hasGroundingMemory);
        }

        return hasSourceEvidence || hasGroundingMemory;
    }

    private static bool MatchesDomain(RetrievalResult result, IReadOnlyList<string> domainTerms)
    {
        var haystack = $"{result.DocId} {result.ChunkId} {result.Content}";
        return MatchesRequiredDomainTerms(haystack, domainTerms);
    }

    private static bool MatchesDomain(GraphEvidence evidence, IReadOnlyList<string> domainTerms)
    {
        var haystack = $"{evidence.Subject} {evidence.Relation} {evidence.Object} {evidence.Template} {evidence.SourceDocument} {evidence.SourcePackTarget} {evidence.TargetPackTarget}";
        return MatchesRequiredDomainTerms(haystack, domainTerms);
    }

    private static bool MatchesDomain(GovernedSourceResult result, IReadOnlyList<string> domainTerms)
    {
        var haystack = $"{result.SourceId} {result.ChunkId} {result.SourceType} {result.Text}";
        return MatchesRequiredDomainTerms(haystack, domainTerms);
    }

    private static bool MatchesDomain(GovernedMemoryResult result, IReadOnlyList<string> domainTerms)
    {
        var haystack = $"{result.MemoryId} {result.MemoryScope} {result.SourceType} {result.Text}";
        return MatchesRequiredDomainTerms(haystack, domainTerms);
    }

    private static bool IsActivatedMemoryRecall(AskWorkflowContext context)
    {
        return string.Equals(context.QueryClassification, "memory_graph_request", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.IntentType, "memory_recall", StringComparison.OrdinalIgnoreCase)
               || AskUploadedContextLookupPolicy.IsUploadedContextLookup(context)
               || context.MemoryResults.Any(memory =>
                   string.Equals(memory.MemoryScope, "activated_user_memory", StringComparison.OrdinalIgnoreCase)
                   || memory.SourceType.Contains("activated_user_memory", StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesRequiredDomainTerms(string haystack, IReadOnlyList<string> domainTerms)
    {
        var terms = domainTerms
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Select(term => term.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (terms.Length == 0)
        {
            return false;
        }

        var requiredMatches = terms.Length >= 3 ? 2 : 1;
        return terms.Count(term => ContainsTerm(haystack, term)) >= requiredMatches;
    }

    private static bool ContainsTerm(string haystack, string term)
    {
        return !string.IsNullOrWhiteSpace(term)
               && haystack.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
#nullable disable
