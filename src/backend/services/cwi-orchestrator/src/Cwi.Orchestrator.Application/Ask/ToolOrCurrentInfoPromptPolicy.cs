#nullable enable
using System.Text.RegularExpressions;

namespace Cwi.Orchestrator.Application.Ask;

public sealed record ToolOrCurrentInfoPromptMatch(
    string IntentType,
    string RetrievalDomain,
    IReadOnlyList<string> RetrievalDomainTerms,
    string Reason);

public static class ToolOrCurrentInfoPromptPolicy
{
    private static readonly Regex FreshnessCuePattern = new(
        @"\b(?:current|latest|right\s+now|today|tonight|last\s+night|2026|use\s+(?:the\s+)?web|find\s+out)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex WebOffDirectivePattern = new(
        @"\b(?:web\s+is\s+off|web\s+off|use\s+web\s+off|without\s+web|do\s+not\s+(?:use|search)\s+(?:the\s+)?web)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SearchFallbackSubjectPattern = new(
        @"\b(?:events?|news|headlines?|topics?|status)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyList<ToolPromptRule> DirectToolRules = new[]
    {
        new ToolPromptRule(
            new Regex(@"\b(?:is\s+(?:cwi|the\s+system|the\s+platform)\s+healthy|cwi\s+health|system\s+status)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "system.status",
            "cwi_operations",
            new[] { "system", "health", "status" },
            "Detected a CWI system health request; routing to governed MCP read-only execution."),
        new ToolPromptRule(
            new Regex(@"\b(?:check\s+all\s+nodes?|node\s+health|nodes?\s+down|any\s+nodes?\s+down|are\s+any\s+nodes?\s+down)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "node.health.check",
            "cwi_operations",
            new[] { "node", "health", "status" },
            "Detected a CWI node health request; routing to governed MCP read-only execution."),
        new ToolPromptRule(
            new Regex(@"\b(?:what\s+actions\s+happened|show(?:\s+me)?\s+what\s+actions|show\s+action\s+history|ledger\s+events|action\s+receipts)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "ledger.view",
            "ledger",
            new[] { "ledger", "actions", "receipts" },
            "Detected a ledger view request; routing to governed MCP read-only execution."),
        new ToolPromptRule(
            new Regex(@"\b(?:inspect\s+pack|get\s+pack)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "pack.get",
            "packs",
            new[] { "pack", "inspect" },
            "Detected a pack inspection request; routing to governed MCP read-only execution."),
        new ToolPromptRule(
            new Regex(@"\b(?:list\s+packs?|available\s+packs?|show\s+packs?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "pack.list",
            "packs",
            new[] { "pack", "list" },
            "Detected a pack listing request; routing to governed MCP read-only execution."),
        new ToolPromptRule(
            new Regex(@"\b(?:resolve\s+pin|pin\s+resolve|check\s+pins|pins\s+mount)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "pin.resolve",
            "pins",
            new[] { "pin", "resolve" },
            "Detected a pin resolution request; routing to governed MCP read-only execution."),
        new ToolPromptRule(
            new Regex(@"\b(?:look\s+up|search\s+(?:the\s+)?web|web\s+search|use\s+(?:the\s+)?web|find\s+out|latest\s+(?:information|ai\s+news))\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "search.web",
            "web",
            new[] { "search", "web", "latest" },
            "Detected a web search request; routing to governed MCP read-only execution."),
        new ToolPromptRule(
            new Regex(@"\b(?:weather|wether|forecast|temperature|will\s+it\s+rain|is\s+it\s+cold|need\s+(?:a\s+)?ja?cket|wear\s+(?:a\s+)?jacket)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "weather.get",
            "weather",
            new[] { "weather", "forecast", "temperature" },
            "Detected a weather lookup request; MCP execution remains gated by policy."),
        new ToolPromptRule(
            new Regex(@"\b(?:news\s+today|newz\s+today|latest\s+news|current\s+news|ai\s+news|headlines(?:\s+today)?|what\s+is\s+the\s+news)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "news.get",
            "news",
            new[] { "news", "headlines" },
            "Detected a news lookup request; MCP execution remains gated by policy."),
        new ToolPromptRule(
            new Regex(@"\b(?:who\s+won\s+(?:the\s+)?game|won\s+(?:the\s+)?game\s+last\s+night|game\s+last\s+night|score\s+last\s+night|sports?\s+scores?|team\s+status|standings|nba|nfl|mlb|nhl|wnba|houston\s+rockets|rockets)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "sports.get",
            "sports",
            new[] { "sports", "score", "game" },
            "Detected a sports lookup request; MCP execution remains gated by policy."),
        new ToolPromptRule(
            new Regex(@"\b(?:email\s+chad|send\s+(?:an\s+)?email|email\s+(?:him|her))\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "email.send",
            "email",
            new[] { "email", "send" },
            "Detected an email send request; routing to MCP policy for approval or block.",
            question => !question.Contains(" from ", StringComparison.Ordinal)),
        new ToolPromptRule(
            new Regex(@"\b(?:what\s+do\s+i\s+have\s+(?:today|tomorrow)|what'?s?\s+on\s+my\s+calendar|calendar\s+tomorrow|meetings?\s+tomorrow|my\s+meetings?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "calendar.view",
            "calendar",
            new[] { "calendar", "meeting", "tomorrow" },
            "Detected a calendar-read request; MCP execution remains gated by policy."),
        new ToolPromptRule(
            new Regex(@"\b(?:read\s+my\s+emails?|emails?\s+from|my\s+inbox)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "email.read",
            "email",
            new[] { "email", "inbox" },
            "Detected an email-read request; MCP execution remains gated by policy."),
        new ToolPromptRule(
            new Regex(@"\b(?:start\s+(?:the\s+)?workflow|run\s+(?:the\s+)?workflow|kick\s+off\s+workflow)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "workflow.start",
            "workflow",
            new[] { "workflow", "start" },
            "Detected a workflow start request; routing to MCP policy for approval or block.")
    };

    private static readonly IReadOnlyList<ToolPromptRule> SearchFallbackRules = new[]
    {
        new ToolPromptRule(
            new Regex(@"\b(?:latest|look\s+up|search|current|right\s+now|use\s+the\s+web|find\s+out|2026)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "search.web",
            "web",
            new[] { "search", "web", "latest" },
            "Detected a web search request; routing to governed MCP read-only execution.",
            question => HasFreshnessCue(question) && SearchFallbackSubjectPattern.IsMatch(question))
    };

    public static bool TryClassifyDirectToolIntent(string? normalizedQuestion, out ToolOrCurrentInfoPromptMatch match)
    {
        if (HasWebOffDirective(normalizedQuestion))
        {
            match = default!;
            return false;
        }

        return TryClassify(normalizedQuestion, DirectToolRules, out match);
    }

    public static bool TryClassifySearchFallbackIntent(string? normalizedQuestion, out ToolOrCurrentInfoPromptMatch match)
    {
        if (HasWebOffDirective(normalizedQuestion))
        {
            match = default!;
            return false;
        }

        return TryClassify(normalizedQuestion, SearchFallbackRules, out match);
    }

    public static bool IsRouteProtectionMatch(string? normalizedQuestion)
    {
        return TryClassifyDirectToolIntent(normalizedQuestion, out _)
               || TryClassifySearchFallbackIntent(normalizedQuestion, out _);
    }

    public static bool HasFreshnessCue(string? normalizedQuestion)
    {
        if (string.IsNullOrWhiteSpace(normalizedQuestion))
        {
            return false;
        }

        var question = LanguageNormalizer.NormalizeForRouting(normalizedQuestion);
        return FreshnessCuePattern.IsMatch(question);
    }

    public static bool HasWebOffDirective(string? normalizedQuestion)
    {
        if (string.IsNullOrWhiteSpace(normalizedQuestion))
        {
            return false;
        }

        var question = LanguageNormalizer.NormalizeForRouting(normalizedQuestion);
        return WebOffDirectivePattern.IsMatch(question);
    }

    private static bool TryClassify(
        string? normalizedQuestion,
        IReadOnlyList<ToolPromptRule> rules,
        out ToolOrCurrentInfoPromptMatch match)
    {
        match = default!;
        if (string.IsNullOrWhiteSpace(normalizedQuestion))
        {
            return false;
        }

        var question = LanguageNormalizer.NormalizeForRouting(normalizedQuestion);

        foreach (var rule in rules)
        {
            if (!rule.Pattern.IsMatch(question))
            {
                continue;
            }

            if (rule.AdditionalPredicate is not null && !rule.AdditionalPredicate(question))
            {
                continue;
            }

            match = new ToolOrCurrentInfoPromptMatch(
                rule.IntentType,
                rule.RetrievalDomain,
                rule.RetrievalDomainTerms,
                rule.Reason);
            return true;
        }

        return false;
    }

    private sealed record ToolPromptRule(
        Regex Pattern,
        string IntentType,
        string RetrievalDomain,
        IReadOnlyList<string> RetrievalDomainTerms,
        string Reason,
        Func<string, bool>? AdditionalPredicate = null);
}
#nullable disable
