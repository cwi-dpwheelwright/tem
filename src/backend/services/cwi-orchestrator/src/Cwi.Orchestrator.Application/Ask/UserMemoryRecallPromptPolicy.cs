#nullable enable
using System.Text.RegularExpressions;

namespace Cwi.Orchestrator.Application.Ask;

public sealed record UserMemoryRecallPromptMatch(
    string RetrievalDomain,
    IReadOnlyList<string> RetrievalDomainTerms);

public static class UserMemoryRecallPromptPolicy
{
    private static readonly Regex SelfReferenceRegex = new(
        @"(?:^|\s)(?:i|me|my|myself|we|our)(?:\s|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyList<UserMemoryRecallRule> Rules = new[]
    {
        new UserMemoryRecallRule(
            new Regex(@"\b(?:which\s+memories\s+are\s+relevant|why\s+did\s+you\s+use\s+that\s+memory|what\s+sources\s+did\s+you\s+use|sources?\s+did\s+you\s+use|source\s+presentation)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "source_memory_graph",
            new[] { "source", "memory", "relevance" }),
        new UserMemoryRecallRule(
            new Regex(@"\b(?:sources?\s+for\s+what\s+you\s+(?:know|remember|understand)\s+about\s+me|show\s+sources)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "source_memory_graph",
            new[] { "source", "memory" },
            RequiresSelfReference: true),
        new UserMemoryRecallRule(
            new Regex(@"\b(?:memories\s+contradict|memory\s+conflicts?|sources?\s+disagree|one\s+source\s+says|what\s+should\s+i\s+believe|who\s+owns\s+dashboard\s+delivery|ownership\s+conflict)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "conflict_memory_graph",
            new[] { "conflict", "source", "authority" }),
        new UserMemoryRecallRule(
            new Regex(@"\b(?:current\s+(?:cwi\s+)?priorit(?:y|ies)|my\s+current\s+(?:cwi\s+)?priorit(?:y|ies)|latest\s+cwi\s+project\s+context|what\s+is\s+true\s+right\s+now\s+about\s+the\s+cwi\s+response\s+frameworks|active\s+now|still\s+current)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "active_context_memory_graph",
            new[] { "current", "priority", "active" }),
        new UserMemoryRecallRule(
            new Regex(@"\b(?:used\s+to\s+say|what\s+did\s+memory\s+used\s+to\s+say|what\s+did\s+i\s+believe\s+before|as\s+of\s+last\s+week|historical\s+memory|old\s+memory|what\s+was\s+my\s+main\s+cwi\s+concern)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "temporal_memory_graph",
            new[] { "historical", "current", "temporal" }),
        new UserMemoryRecallRule(
            new Regex(@"\b(?:decisions?\s+have\s+already\s+been\s+made|made\s+about\s+the\s+response\s+frameworks|open\s+product\s+decisions)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "decision_memory_graph",
            new[] { "decision", "framework" }),
        new UserMemoryRecallRule(
            new Regex(@"\b(?:home life|personal life)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "personal_memory_graph",
            new[] { "home", "personal" },
            RequiresSelfReference: true),
        new UserMemoryRecallRule(
            new Regex(@"\b(?:my name|who am i)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "identity_memory_graph",
            new[] { "name", "identity" },
            RequiresSelfReference: true),
        new UserMemoryRecallRule(
            new Regex(@"\b(?:do\s+i\s+do\s+for\s+work|do\s+i\s+do\s+professionally|my\s+job|my\s+profession|my\s+role|as\s+a\s+job)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "work_memory_graph",
            new[] { "work", "role", "job", "profession" },
            RequiresSelfReference: true),
        new UserMemoryRecallRule(
            new Regex(@"\b(?:key\s+people\s+in\s+my\s+network|people\s+in\s+my\s+network|who\s+matters\s+in\s+my\s+network)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "relationship_memory_graph",
            new[] { "person", "relationship" }),
        new UserMemoryRecallRule(
            new Regex(@"\brelationships?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "relationship_memory_graph",
            new[] { "person", "relationship" },
            RequiresSelfReference: true),
        new UserMemoryRecallRule(
            new Regex(@"\b(?:working\s+on|projects?|work life)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "work_memory_graph",
            new[] { "work", "project" },
            RequiresSelfReference: true),
        new UserMemoryRecallRule(
            new Regex(@"\b(?:open loops?|next actions?|need\s+to\s+do)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "open_loop_memory_graph",
            new[] { "open", "need" }),
        new UserMemoryRecallRule(
            new Regex(@"\bgoals?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "goal_memory_graph",
            new[] { "goal" },
            RequiresSelfReference: true),
        new UserMemoryRecallRule(
            new Regex(@"\b(?:(?:know|remember|understand|learned|learnt)\s+about|understand\s+of|describe|kind\s+of\s+person|personality|tell\s+me\s+about\s+myself|who\s+am\s+i|my\s+name)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "user_memory_graph",
            new[] { "user" },
            RequiresSelfReference: true)
    };

    public static bool IsMatch(string? normalizedQuestion)
    {
        return TryClassify(normalizedQuestion, out _);
    }

    public static bool TryClassify(string? normalizedQuestion, out UserMemoryRecallPromptMatch match)
    {
        match = default!;
        if (string.IsNullOrWhiteSpace(normalizedQuestion))
        {
            return false;
        }

        foreach (var rule in Rules)
        {
            if (rule.RequiresSelfReference && !SelfReferenceRegex.IsMatch(normalizedQuestion))
            {
                continue;
            }

            if (!rule.Pattern.IsMatch(normalizedQuestion))
            {
                continue;
            }

            match = new UserMemoryRecallPromptMatch(rule.RetrievalDomain, rule.RetrievalDomainTerms);
            return true;
        }

        return false;
    }

    private sealed record UserMemoryRecallRule(
        Regex Pattern,
        string RetrievalDomain,
        IReadOnlyList<string> RetrievalDomainTerms,
        bool RequiresSelfReference = false);
}
#nullable disable
