#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Workflows;

namespace Cwi.Orchestrator.Application.Runtime;

public sealed class TruthEngineService
{
    public const string Version = "truth-engine-observe-v1";

    private static readonly HashSet<string> HistoricalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "historical",
        "history",
        "became_historical"
    };

    private static readonly HashSet<string> StaleStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "stale",
        "expired",
        "superseded",
        "retracted"
    };

    private static readonly HashSet<string> ConflictStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "conflict",
        "conflicted",
        "contradicted",
        "contradictory"
    };

    public TruthEngineDecisionDto Evaluate(
        AskWorkflowContext context,
        SelectiveEnforcementMode mode)
    {
        ArgumentNullException.ThrowIfNull(context);

        var evidenceKinds = ResolveEvidenceKinds(context);
        var conflictCount = CountConflicts(context);
        var state = ResolveState(context, evidenceKinds, conflictCount);
        var reason = ResolveReason(context, state, evidenceKinds, conflictCount);

        return new TruthEngineDecisionDto(
            Version,
            NormalizeMode(mode),
            state,
            reason,
            evidenceKinds.Count,
            conflictCount,
            evidenceKinds,
            DateTimeOffset.UtcNow);
    }

    private static string ResolveState(
        AskWorkflowContext context,
        IReadOnlyCollection<string> evidenceKinds,
        int conflictCount)
    {
        if (conflictCount > 0)
        {
            return "conflicted";
        }

        if (context.EvidenceRequired && !AskEvidencePolicy.HasGroundedEvidence(context))
        {
            return "unsupported";
        }

        var hasCurrentEvidence = HasCurrentEvidence(context, evidenceKinds);
        if (HasAnyTemporalState(context, StaleStates) && !hasCurrentEvidence)
        {
            return "stale";
        }

        if (HasAnyTemporalState(context, HistoricalStates) && !hasCurrentEvidence)
        {
            return "historical";
        }

        if (hasCurrentEvidence)
        {
            return "current";
        }

        if (IsEstimatedReasoning(context))
        {
            return "estimated";
        }

        return "unsupported";
    }

    private static string ResolveReason(
        AskWorkflowContext context,
        string state,
        IReadOnlyCollection<string> evidenceKinds,
        int conflictCount)
    {
        return state switch
        {
            "conflicted" => $"conflicting_evidence_present:{conflictCount}",
            "unsupported" when context.EvidenceRequired => "required_evidence_missing",
            "unsupported" => "no_active_evidence_or_supported_truth_basis",
            "stale" => "only_stale_or_superseded_context_available",
            "historical" => "only_historical_context_available",
            "current" => $"current_evidence_available:{string.Join(",", evidenceKinds.OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase))}",
            "estimated" => "reasoning_or_general_knowledge_without_governed_source_evidence",
            _ => "truth_state_observed"
        };
    }

    private static List<string> ResolveEvidenceKinds(AskWorkflowContext context)
    {
        var kinds = new List<string>();
        if (context.GraphEvidence.Count > 0)
        {
            kinds.Add("graph");
        }

        if (context.Retrieved.Count > 0)
        {
            kinds.Add("document");
        }

        if (context.SourceResults.Count > 0)
        {
            kinds.Add("source");
        }

        if (context.MemoryResults.Count > 0)
        {
            kinds.Add("memory");
        }

        if (context.TrustedWebResults.Count > 0)
        {
            kinds.Add("trusted_web");
        }

        if (context.HomeNodeSources.Count > 0)
        {
            kinds.Add("home_node");
        }

        if (HasSystemTruthGrounding(context))
        {
            kinds.Add("system_truth");
        }

        if (context.MemoryUpdated || string.Equals(context.GroundingMode, "memory_update", StringComparison.OrdinalIgnoreCase))
        {
            kinds.Add("memory_update");
        }

        return kinds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool HasCurrentEvidence(
        AskWorkflowContext context,
        IReadOnlyCollection<string> evidenceKinds)
    {
        if (evidenceKinds.Count == 0)
        {
            return false;
        }

        if (context.MemoryFrameworkDecisions.Count == 0)
        {
            return true;
        }

        return context.MemoryFrameworkDecisions.Any(decision =>
            string.Equals(decision.Outcome, "active", StringComparison.OrdinalIgnoreCase)
            || string.Equals(decision.TemporalState, "current", StringComparison.OrdinalIgnoreCase));
    }

    private static int CountConflicts(AskWorkflowContext context)
    {
        return context.MemoryConflictSummaries.Count
               + context.MemoryFrameworkDecisions.Count(decision =>
                   ConflictStates.Contains(decision.TemporalState)
                   || ConflictStates.Contains(decision.Outcome));
    }

    private static bool HasAnyTemporalState(
        AskWorkflowContext context,
        ISet<string> states)
    {
        return context.MemoryFrameworkDecisions.Any(decision =>
            states.Contains(decision.TemporalState)
            || states.Contains(decision.Outcome));
    }

    private static bool IsEstimatedReasoning(AskWorkflowContext context)
    {
        if (string.Equals(context.QueryClassification, "deterministic_datetime", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "deterministic_reasoning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "code_generation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "code_generation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "general_reasoning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "general_model_knowledge", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.GroundingMode, "non_grounded", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !context.EvidenceRequired
               && !AskEvidencePolicy.HasGroundedEvidence(context);
    }

    private static bool HasSystemTruthGrounding(AskWorkflowContext context)
    {
        return context.QueryClassification is "identity_definition" or "home_node_definition" or "system_truth"
               && context.BaselinePackGuidance.Any(pack =>
                   pack.HasRole("system_truth")
                   || string.Equals(pack.PackName, "cwi-identity", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeMode(SelectiveEnforcementMode mode)
    {
        return mode.ToString().Trim().ToLowerInvariant();
    }
}
#nullable disable
