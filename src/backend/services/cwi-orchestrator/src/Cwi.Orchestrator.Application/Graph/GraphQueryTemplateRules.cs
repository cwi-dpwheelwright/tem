using System.Text.RegularExpressions;

namespace Cwi.Orchestrator.Application.Graph;

public interface IGraphQueryTemplateRule
{
    int Order { get; }

    string Template { get; }

    bool UsesUserMemory { get; }

    IReadOnlyList<string> DefaultTerms { get; }

    string CategoryFilter { get; }

    bool Matches(string normalizedQuestion);
}

public sealed class RegexGraphQueryTemplateRule : IGraphQueryTemplateRule
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);
    private readonly IReadOnlyList<Regex> _patterns;

    public RegexGraphQueryTemplateRule(
        int order,
        string template,
        bool usesUserMemory,
        IReadOnlyList<string> defaultTerms,
        string categoryFilter,
        IReadOnlyList<string> patterns)
    {
        Order = order;
        Template = template;
        UsesUserMemory = usesUserMemory;
        DefaultTerms = defaultTerms;
        CategoryFilter = categoryFilter;
        _patterns = patterns
            .Select(pattern => new Regex(
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                MatchTimeout))
            .ToArray();
    }

    public int Order { get; }

    public string Template { get; }

    public bool UsesUserMemory { get; }

    public IReadOnlyList<string> DefaultTerms { get; }

    public string CategoryFilter { get; }

    public bool Matches(string normalizedQuestion)
    {
        return !string.IsNullOrWhiteSpace(normalizedQuestion)
               && _patterns.Any(pattern => pattern.IsMatch(normalizedQuestion));
    }
}

public static class GraphQueryTemplateRules
{
    public static IReadOnlyList<IGraphQueryTemplateRule> Defaults { get; } =
    [
        UserMemory(
            10,
            "daily_briefing",
            ["deadline", "due"],
            "coalesce(m.memory_category, '') IN ['open_loop', 'goal', 'responsibility'] OR coalesce(m.memory_type, '') IN ['task_memory', 'issue_memory', 'event_memory', 'financial_memory', 'goal_memory', 'routine_memory'] OR toLower(coalesce(m.fact_text, '')) CONTAINS 'deadline' OR toLower(coalesce(m.fact_text, '')) CONTAINS 'due' OR toLower(coalesce(m.fact_text, '')) CONTAINS 'blocked' OR toLower(coalesce(m.fact_text, '')) CONTAINS 'risk'",
            [
                @"\b(?:daily|morning|today'?s?|todays)\s+briefing\b",
                @"\bbrief\s+me(?:\s+for\s+today)?\b",
                @"\bwhat\s+(?:should\s+i\s+do|needs\s+my\s+attention|should\s+i\s+focus\s+on)\s+today\b",
                @"\bstart\s+my\s+day\b"
            ]),
        UserMemory(
            20,
            "risks",
            ["risk", "blocked"],
            "coalesce(m.memory_type, '') IN ['issue_memory', 'safety_security_memory', 'financial_memory'] OR toLower(coalesce(m.fact_text, '')) CONTAINS 'risk' OR toLower(coalesce(m.fact_text, '')) CONTAINS 'blocked' OR toLower(coalesce(m.fact_text, '')) CONTAINS 'deadline' OR toLower(coalesce(m.fact_text, '')) CONTAINS 'due'",
            [
                @"\b(?:risk|risks|at\s+risk|blocked|blocker|deadline\s+risk)\b",
                @"\bwhat\s+should\s+i\s+worry\s+about\b"
            ]),
        UserMemory(
            30,
            "recent_changes",
            ["recent", "change"],
            string.Empty,
            [
                @"\bwhat\s+changed\b",
                @"\bchanged\s+since\b",
                @"\bnew\s+since\b",
                @"\bsince\s+yesterday\b",
                @"\brecent\s+changes\b"
            ]),
        UserMemory(
            40,
            "priorities",
            ["priority", "deadline"],
            "coalesce(m.memory_category, '') IN ['open_loop', 'goal'] OR coalesce(m.memory_type, '') IN ['task_memory', 'issue_memory', 'event_memory', 'financial_memory', 'goal_memory'] OR toLower(coalesce(m.fact_text, '')) CONTAINS 'deadline' OR toLower(coalesce(m.fact_text, '')) CONTAINS 'due'",
            [
                @"\bfocus\s+on\s+first\b",
                @"\bwhat\s+should\s+i\s+focus\b",
                @"\bprioriti(?:ze|se|y|es)\b",
                @"\brecommended\s+order\b"
            ]),
        UserMemory(
            50,
            "open_loops",
            ["open", "need"],
            "coalesce(m.memory_category, '') = 'open_loop' OR coalesce(m.memory_type, '') IN ['task_memory', 'issue_memory'] OR toLower(coalesce(m.fact_text, '')) CONTAINS 'follow up' OR toLower(coalesce(m.fact_text, '')) CONTAINS 'need to'",
            [
                @"\bopen\s+loops?\b",
                @"\bneed\s+to\s+do\b",
                @"\bnext\s+actions?\b",
                @"\bthis\s+week\b",
                @"\bto\s*do\b",
                @"\btodo\b"
            ]),
        UserMemory(
            60,
            "goals",
            ["goal"],
            "coalesce(m.memory_category, '') = 'goal' OR coalesce(m.memory_type, '') = 'goal_memory'",
            [
                @"\bgoals?\b",
                @"\bobjectives?\b",
                @"\btrying\s+to\b",
                @"\bwant\s+to\s+accomplish\b"
            ]),
        UserMemory(
            70,
            "decisions",
            ["decision"],
            "coalesce(m.memory_category, '') = 'decision' OR coalesce(m.memory_type, '') = 'decision_memory'",
            [
                @"\bdecisions?\b",
                @"\bwhat\s+did\s+(?:i|we)\s+decide\b"
            ]),
        UserMemory(
            80,
            "home_life",
            ["home", "personal"],
            "coalesce(m.memory_domain, '') IN ['personal', 'home', 'family', 'pets', 'food_meals', 'health_wellness', 'finance', 'vehicles', 'school_learning'] OR coalesce(m.memory_category, '') IN ['home_life', 'responsibility']",
            [
                @"\b(?:home|personal)\s+life\b",
                @"\bhome\s+responsibilities\b",
                @"\broutines?\b",
                @"\bfamily\b",
                @"\bhouse\b"
            ]),
        UserMemory(
            90,
            "work_life",
            ["work", "project"],
            "coalesce(m.memory_domain, '') IN ['work', 'work_career', 'business', 'technology', 'operations', 'legal_ip'] OR coalesce(m.memory_category, '') IN ['work_life', 'current_work', 'responsibility']",
            [
                @"\bwork\s+(?:life|context|responsibilities|projects)\b",
                @"\bprofessional\s+context\b"
            ]),
        UserMemory(
            100,
            "current_work",
            ["work", "project"],
            "coalesce(m.memory_domain, '') IN ['work', 'work_career', 'business', 'technology', 'operations'] OR coalesce(m.memory_category, '') IN ['current_work', 'work_life', 'open_loop'] OR coalesce(m.memory_type, '') IN ['project_memory', 'task_memory', 'issue_memory']",
            [
                @"\bworking\s+on\b",
                @"\bworked\s+on\b",
                @"\bbeen\s+working\b",
                @"\bprojects?\b",
                @"\bcurrent\s+work\b"
            ]),
        UserMemory(
            110,
            "user_profile",
            ["user"],
            string.Empty,
            [
                @"\bwhat\s+do\s+you\s+(?:know|remember|understand)\s+about\s+me\b",
                @"\bwhat\s+(?:do\s+you\s+know|do\s+you\s+understand)\s+about\s+my\b",
                @"\bwhat\s+have\s+you\s+(?:learned|learnt)\s+about\s+me\b",
                @"\bdescribe\s+me\b"
            ]),
        Evidence(
            200,
            "maintenance_attention",
            ["maintenance", "preventive", "pm", "filter", "duration", "pressure", "service"],
            [
                @"\bmaintenance\b",
                @"\bpreventive\b",
                @"\bpm\b",
                @"\bfilter\b",
                @"\bduration\b",
                @"\bpressure\b",
                @"\bservice\b"
            ]),
        Evidence(
            210,
            "procedure_family",
            ["procedure", "procedures", "family", "training", "checklist", "checklists"],
            [
                @"\bprocedures?\b",
                @"\bfamily\b",
                @"\btraining\b",
                @"\bchecklists?\b"
            ]),
        Evidence(
            220,
            "equipment_metrics",
            ["equipment", "operations", "monitored", "monitoring", "metric", "metrics", "amperage", "hz", "frequency"],
            [
                @"\bequipment\b",
                @"\boperations\b",
                @"\b(?:monitored|monitoring)\b",
                @"\bmetrics?\b",
                @"\bamperage\b",
                @"\bhz\b",
                @"\bfrequency\b"
            ])
    ];

    private static RegexGraphQueryTemplateRule UserMemory(
        int order,
        string template,
        IReadOnlyList<string> defaultTerms,
        string categoryFilter,
        IReadOnlyList<string> patterns)
    {
        return new RegexGraphQueryTemplateRule(order, template, true, defaultTerms, categoryFilter, patterns);
    }

    private static RegexGraphQueryTemplateRule Evidence(
        int order,
        string template,
        IReadOnlyList<string> defaultTerms,
        IReadOnlyList<string> patterns)
    {
        return new RegexGraphQueryTemplateRule(order, template, false, defaultTerms, string.Empty, patterns);
    }
}
