#nullable enable
using System.Text;
using System.Text.RegularExpressions;
using Cwi.Orchestrator.Application.Packs;

namespace Cwi.Orchestrator.Application.Ask;

public sealed record AskQueryClassification(
    string Classification,
    string Reason,
    string? IntentType,
    string? AudienceMode,
    string? VerbosityMode,
    bool RetrievalAllowed,
    string GroundingMode,
    string? RetrievalDomain,
    IReadOnlyList<string> RetrievalDomainTerms,
    string? RetrievalSkippedReason,
    float RetrievalMinScore,
    bool EvidenceRequired,
    string? MemoryUpdateKind,
    string? MemoryUpdateValue);

public sealed class AskQueryClassifier
{
    private const float DefaultRetrievalMinScore = 0.35f;
    private static readonly Regex ControlledFactEntityRegex = new(
        @"\bcwi[-\s]?rl[-\s]?\d{3,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ControlledFactTermRegex = new(
        "[A-Za-z0-9][A-Za-z0-9_-]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] HomeNodePatterns =
    {
        "what is a cwi home node",
        "what is the cwi home node",
        "how does the home node work",
        "what is a home node",
        "how does cwi home node work"
    };

    private static readonly string[] PreferencePatterns =
    {
        "my preferred",
        "my preference",
        "i prefer",
        "please remember",
        "remember that",
        "from now on",
        "going forward",
        "answer me in",
        "format responses",
        "format answers",
        "i want responses",
        "i want answers"
    };

    private static readonly string[] ExplanationPatterns =
    {
        "explain",
        "what is",
        "what does",
        "how does",
        "define",
        "tell me about",
        "walk me through"
    };

    private static readonly string[] OperationalPatterns =
    {
        "deploy",
        "configure",
        "set up",
        "setup",
        "install",
        "run",
        "route",
        "monitor",
        "status",
        "validate",
        "reload"
    };

    private static readonly string[] TroubleshootingPatterns =
    {
        "error",
        "issue",
        "problem",
        "failing",
        "failed",
        "broken",
        "fix",
        "debug",
        "why isn't",
        "not working",
        "bad request"
    };

    private static readonly string[] DecisionSupportPatterns =
    {
        "business impact",
        "board",
        "board update",
        "briefing",
        "impact",
        "investor",
        "investor demo",
        "investors",
        "summarize",
        "summary",
        "executive summary",
        "executive briefing",
        "high level",
        "high-level",
        "leadership",
        "stakeholder",
        "stakeholders",
        "should we",
        "recommend",
        "recommendation",
        "tradeoff",
        "decision"
    };

    private static readonly string[] SourceSeekingPatterns =
    {
        "according to",
        "based on",
        "from the docs",
        "from our docs",
        "using the docs",
        "using our docs",
        "in the docs",
        "from the repo",
        "in the repo",
        "source",
        "sources",
        "citation",
        "cite",
        "evidence",
        "runbook",
        "policy",
        "procedure",
        "procedures",
        "manual",
        "logs",
        "trace",
        "iteration"
    };

    private static readonly string[] RiskModePatterns =
    {
        "what is at risk",
        "what's at risk",
        "at risk right now",
        "risk right now",
        "risks right now",
        "what is the risk",
        "what are the risks",
        "what is at risk with",
        "at risk with",
        "any risks",
        "what should i worry about"
    };

    private static readonly string[] DailyBriefingPatterns =
    {
        "daily briefing",
        "today's briefing",
        "todays briefing",
        "morning briefing",
        "brief me",
        "brief me for today",
        "what should i do today",
        "what needs my attention today",
        "what should i focus on today",
        "start my day"
    };

    private static readonly string[] ChangeReviewPatterns =
    {
        "what changed",
        "what's changed",
        "what has changed",
        "what changed today",
        "changed since",
        "since yesterday",
        "recent changes"
    };

    private static readonly string[] PlanModePatterns =
    {
        "make a plan",
        "build a plan",
        "create a plan",
        "help me make a plan",
        "plan my day",
        "help me plan",
        "what should i do next",
        "what should i focus on first",
        "prioritize my day"
    };

    private static readonly string[] SummarizationPatterns =
    {
        "summarize",
        "summarise",
        "summary of",
        "short words",
        "sum up",
        "tl dr",
        "tldr"
    };

    private static readonly string[] CodeGenerationPatterns =
    {
        "code",
        "python",
        "function",
        "script",
        "program",
        "reverse a string",
        "factorial",
        "list filtering",
        "filter a list"
    };

    private static readonly Regex[] MessageDraftPromptPatterns =
    {
        new(@"\b(?:write|draft|compose|word)\b.*\b(?:message|text|email|note|reply)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bhelp\s+me\s+(?:write|draft|word|say|tell|explain|ask)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bwhat\s+(?:should|do)\s+i\s+(?:say|text|write)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bhow\s+do\s+i\s+(?:ask|tell|apologize|apologise|cancel|say\s+no|explain)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bi\s+need\s+to\s+explain\s+that\s+i\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bturn\b.*\binto\s+a\s+short\s+(?:message|text|note|reply)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bturn\b.*\binto\s+a\s+(?:message|text|note|reply)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bsendable\s+(?:message|text|note|reply)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\b(?:want|need)\s+to\s+ask\s+for\s+help\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bi\s+need\s+to\s+(?:apologize|apologise)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bi\s+need\s+to\s+ask\s+for\s+more\s+time\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bi\s+need\s+to\s+ask\b.*\b(?:someone|friend|partner|manager|teacher|parent|person|them)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bi\s+need\s+to\s+(?:ask|tell|cancel|apologize|apologise|explain|say)\b.*\b(?:without|politely|respectful|respectfully|message|text|email|someone|teacher|manager|friend)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
    };

    private static readonly string[] FoodSafetyPatterns =
    {
        "leftovers",
        "still safe to eat",
        "safe to eat",
        "safe to drink",
        "gone bad",
        "four days ago",
        "4 days ago",
        "old food",
        "food poisoning",
        "expired",
        "sell by",
        "use by"
    };

    private static readonly string[] PracticalGuidanceCuePatterns =
    {
        "what should i",
        "what should i do first",
        "what should i try first",
        "what should i do before",
        "should i",
        "how should i",
        "what do i",
        "help me",
        "help me decide",
        "give me",
        "what is the fastest",
        "fastest plan",
        "fastest fix",
        "quick temporary fix",
        "temporary fix",
        "safest quick cleanup",
        "cleanest way",
        "compare",
        "single action",
        "what single action",
        "first two things",
        "best search plan",
        "starting point",
        "highest-value",
        "highest value",
        "what order",
        "order should i",
        "prioritize",
        "decide",
        "talk me through",
        "need help planning",
        "planning",
        "plan a",
        "plan for",
        "plan my",
        "figure it out"
    };

    private static readonly string[] EverydayLifeContextPatterns =
    {
        "apartment",
        "a lot to do",
        "backpack",
        "bank account",
        "bank charge",
        "bedtime",
        "birthday",
        "bus",
        "classmate",
        "clean",
        "cleaning",
        "clothes",
        "coffee stain",
        "cooked chicken",
        "debit card",
        "deadline",
        "dinner",
        "double-booked",
        "double booked",
        "email inbox",
        "event",
        "friend",
        "gas",
        "glass",
        "groceries",
        "hangout",
        "headache",
        "homework",
        "hoodie",
        "keys",
        "laundry",
        "left something",
        "locked the door",
        "lost my",
        "lunch",
        "manager",
        "messages",
        "milk",
        "money",
        "morning routine",
        "notebook",
        "parent",
        "phone",
        "phone number",
        "phones",
        "plans fell apart",
        "practice",
        "quiz",
        "reschedule",
        "ride",
        "room",
        "routine",
        "school",
        "school id",
        "shirt",
        "shoes",
        "slept",
        "strap",
        "spilled",
        "study",
        "student id",
        "teacher",
        "test",
        "text",
        "used phone",
        "used phones",
        "wallet"
    };

    private static readonly string[] CurrentEventsPatterns =
    {
        "current topics",
        "current events",
        "world current topics",
        "world's current topics",
        "world topics",
        "latest news",
        "today's news",
        "todays news",
        "what is happening",
        "what's happening",
        "what is going on in the world",
        "what are people talking about"
    };

    private static readonly string[] DomainSignalPatterns =
    {
        "api",
        "backend",
        "bluetooth",
        "container",
        "curl",
        "cutover",
        "docker",
        "gateway",
        "graph",
        "home node",
        "identity",
        "iteration",
        "kubernetes",
        "ledger",
        "local wifi",
        "offline",
        "orchestrator",
        "core pack",
        "domain pack",
        "pack adoption",
        "pack hash",
        "pack manifest",
        "pack resolver",
        "pack service",
        "pack verification",
        "qdrant",
        "repo",
        "router",
        "server cutover",
        "teacher node",
        "tooling",
        "vector",
        "wifi"
    };

    private static readonly HashSet<string> DomainStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "after", "again", "also", "answer", "answers", "before", "best", "could", "create",
        "deployment", "detailed", "give", "help", "instructions", "make", "need", "plan", "please",
        "preferred", "question", "request", "response", "responses", "runbook", "server", "should", "step",
        "steps", "through", "using", "want", "would"
    };

    public AskQueryClassification Classify(
        string? question,
        IReadOnlyCollection<PackManifestMetadata>? baselinePackMetadata = null)
    {
        var primaryQuestion = AskQuestionScope.ExtractPrimaryQuestion(question);
        var normalized = Normalize(primaryQuestion);
        var intentType = DetectIntentType(normalized);
        var audienceMode = DetectAudienceMode(normalized);
        var verbosityMode = DetectVerbosityMode(normalized);
        var sourceSeeking = ContainsAny(normalized, SourceSeekingPatterns);

        if (TryClassifyControlledFact(
                primaryQuestion,
                normalized,
                audienceMode,
                verbosityMode,
                out var controlledFactClassification))
        {
            return controlledFactClassification;
        }

        if (TryClassifyMemoryUpdate(question, primaryQuestion, normalized, audienceMode, verbosityMode, out var memoryUpdate)
            || TryClassifyDeterministic(primaryQuestion, audienceMode, verbosityMode, out memoryUpdate)
            || TryClassifyBaselineSystemTruth(primaryQuestion, normalized, baselinePackMetadata, intentType, audienceMode, verbosityMode, out memoryUpdate)
            || TryClassifyDirectSystemTruth(normalized, intentType, audienceMode, verbosityMode, out memoryUpdate)
            || TryClassifyEverydayResponseMode(intentType, audienceMode, verbosityMode, out memoryUpdate)
            || TryClassifyProactiveMode(normalized, audienceMode, out memoryUpdate)
            || TryClassifyToolAndSpecialModes(normalized, intentType, audienceMode, verbosityMode, out memoryUpdate))
        {
            return memoryUpdate;
        }

        return ClassifyDomainOrGeneral(normalized, intentType, audienceMode, verbosityMode, sourceSeeking);
    }

    private static bool TryClassifyMemoryUpdate(
        string? question,
        string primaryQuestion,
        string normalized,
        string? audienceMode,
        string? verbosityMode,
        out AskQueryClassification classification)
    {
        if (IsPreferenceUpdate(normalized))
        {
            classification = new AskQueryClassification(
                "preference_update",
                "Detected an operator preference statement; routing to memory update.",
                "memory_update",
                audienceMode,
                verbosityMode,
                RetrievalAllowed: false,
                GroundingMode: "memory_update",
                RetrievalDomain: "operator_preferences",
                RetrievalDomainTerms: Array.Empty<string>(),
                RetrievalSkippedReason: "preference_update_routes_to_memory",
                RetrievalMinScore: DefaultRetrievalMinScore,
                EvidenceRequired: false,
                MemoryUpdateKind: "output_style",
                MemoryUpdateValue: DetectPreferenceValue(question, normalized));
            return true;
        }

        if (IsSelfContextDisclosure(primaryQuestion, normalized))
        {
            classification = new AskQueryClassification(
                "self_context_update",
                "Detected a first-person durable context disclosure; acknowledging it and routing to governed user memory update without retrieval.",
                "memory_update",
                audienceMode,
                verbosityMode,
                RetrievalAllowed: false,
                GroundingMode: "memory_update",
                RetrievalDomain: "user_context",
                RetrievalDomainTerms: ExtractSelfContextTerms(normalized),
                RetrievalSkippedReason: "self_context_update_routes_to_memory",
                RetrievalMinScore: DefaultRetrievalMinScore,
                EvidenceRequired: false,
                MemoryUpdateKind: "user_context",
                MemoryUpdateValue: primaryQuestion.Trim());
            return true;
        }

        return NoClassification(out classification);
    }

    private static bool TryClassifyDeterministic(
        string primaryQuestion,
        string? audienceMode,
        string? verbosityMode,
        out AskQueryClassification classification)
    {
        if (!IsSimpleArithmetic(primaryQuestion))
        {
            return NoClassification(out classification);
        }

        classification = new AskQueryClassification(
            "deterministic_reasoning",
            "Detected simple deterministic arithmetic; retrieval is not required.",
            "deterministic_reasoning",
            audienceMode,
            verbosityMode,
            RetrievalAllowed: false,
            GroundingMode: "non_grounded",
            RetrievalDomain: null,
            RetrievalDomainTerms: Array.Empty<string>(),
            RetrievalSkippedReason: "deterministic_reasoning_does_not_require_sources",
            RetrievalMinScore: DefaultRetrievalMinScore,
            EvidenceRequired: false,
            MemoryUpdateKind: null,
            MemoryUpdateValue: null);
        return true;
    }

    private static bool TryClassifyBaselineSystemTruth(
        string primaryQuestion,
        string normalized,
        IReadOnlyCollection<PackManifestMetadata>? baselinePackMetadata,
        string intentType,
        string? audienceMode,
        string? verbosityMode,
        out AskQueryClassification classification)
    {
        if (baselinePackMetadata is null)
        {
            return NoClassification(out classification);
        }

        foreach (var metadata in baselinePackMetadata)
        {
            if (!metadata.MatchesForceQuery(primaryQuestion))
            {
                continue;
            }

            if (IsDirectHomeNodeQuestion(normalized))
            {
                classification = BuildHomeNodeDefinitionClassification(
                    "Matched governed force-query pattern for home-node identity.",
                    intentType,
                    audienceMode,
                    verbosityMode);
                return true;
            }

            if (IsDirectIdentityQuestion(normalized))
            {
                classification = BuildIdentityDefinitionClassification(
                    "Matched governed force-query pattern for system identity.",
                    intentType,
                    audienceMode,
                    verbosityMode);
                return true;
            }
        }

        return NoClassification(out classification);
    }

    private static bool TryClassifyDirectSystemTruth(
        string normalized,
        string intentType,
        string? audienceMode,
        string? verbosityMode,
        out AskQueryClassification classification)
    {
        if (IsDirectHomeNodeQuestion(normalized))
        {
            classification = BuildHomeNodeDefinitionClassification(
                "Question targets home-node identity or behavior.",
                intentType,
                audienceMode,
                verbosityMode);
            return true;
        }

        if (IsCwiMemoryActivationQuestion(normalized))
        {
            classification = new AskQueryClassification(
                "cwi_memory_activation_explanation",
                "Question targets CWI memory activation behavior; answer from governed system architecture.",
                intentType,
                audienceMode,
                verbosityMode,
                RetrievalAllowed: false,
                GroundingMode: "system_truth",
                RetrievalDomain: "cwi_memory_activation",
                RetrievalDomainTerms: new[] { "cwi", "memory", "activation", "retrieval", "governance" },
                RetrievalSkippedReason: "system_truth_uses_governed_memory_architecture",
                RetrievalMinScore: DefaultRetrievalMinScore,
                EvidenceRequired: false,
                MemoryUpdateKind: null,
                MemoryUpdateValue: null);
            return true;
        }

        if (IsDirectIdentityQuestion(normalized))
        {
            classification = BuildIdentityDefinitionClassification(
                "Question targets CWI identity or definition.",
                intentType,
                audienceMode,
                verbosityMode);
            return true;
        }

        return NoClassification(out classification);
    }

    private static bool TryClassifyEverydayResponseMode(
        string intentType,
        string? audienceMode,
        string? verbosityMode,
        out AskQueryClassification classification)
    {
        if (string.Equals(intentType, "message_draft", StringComparison.OrdinalIgnoreCase))
        {
            classification = BuildGeneralNonRetrievalClassification(
                "Detected a message-drafting request; answer from the current prompt without enterprise retrieval.",
                intentType,
                audienceMode,
                verbosityMode ?? "concise",
                "message_draft_uses_current_prompt");
            return true;
        }

        if (string.Equals(intentType, "food_safety", StringComparison.OrdinalIgnoreCase))
        {
            classification = BuildGeneralNonRetrievalClassification(
                "Detected a food-safety question; answer cautiously without enterprise retrieval.",
                intentType,
                audienceMode,
                verbosityMode ?? "concise",
                "food_safety_uses_general_safety_guidance");
            return true;
        }

        if (string.Equals(intentType, "practical_guidance", StringComparison.OrdinalIgnoreCase))
        {
            classification = BuildGeneralNonRetrievalClassification(
                "Detected an everyday practical guidance request; answer directly without ops, proactive, or enterprise retrieval.",
                intentType,
                audienceMode,
                verbosityMode ?? "concise",
                "everyday_practical_guidance_uses_current_prompt");
            return true;
        }

        return NoClassification(out classification);
    }

    private static bool TryClassifyProactiveMode(
        string normalized,
        string? audienceMode,
        out AskQueryClassification classification)
    {
        if (IsDailyBriefingPrompt(normalized))
        {
            classification = BuildProactiveClassification(
                "daily_briefing",
                "daily_briefing",
                new[] { "deadline", "due", "priority" },
                "Current prompt asks for the daily briefing response mode; routing to proactive intelligence APIs.",
                audienceMode: "executive");
            return true;
        }

        if (IsRiskModePrompt(normalized))
        {
            classification = BuildRiskModeClassification(normalized, audienceMode);
            return true;
        }

        if (IsChangeReviewPrompt(normalized))
        {
            classification = BuildProactiveClassification(
                "change_review",
                "recent_changes",
                new[] { "changed", "recent", "new" },
                "Current prompt asks for what changed; routing to proactive intelligence APIs.",
                audienceMode);
            return true;
        }

        if (IsPlanModePrompt(normalized))
        {
            classification = BuildPlanModeClassification(normalized, audienceMode);
            return true;
        }

        return NoClassification(out classification);
    }

    private static bool TryClassifyToolAndSpecialModes(
        string normalized,
        string intentType,
        string? audienceMode,
        string? verbosityMode,
        out AskQueryClassification classification)
    {
        if (IsCurrentInfoPromptWithWebDisabled(normalized))
        {
            classification = BuildCurrentEventsWebDisabledClassification(audienceMode);
            return true;
        }

        if (ToolOrCurrentInfoPromptPolicy.TryClassifyDirectToolIntent(normalized, out var directToolIntent))
        {
            classification = BuildToolIntentClassification(directToolIntent);
            return true;
        }

        if (string.Equals(intentType, "needs_clarification", StringComparison.OrdinalIgnoreCase)
            || IsAmbiguousActionPrompt(normalized))
        {
            classification = BuildClarificationClassification(audienceMode, verbosityMode);
            return true;
        }

        if (IsPlainSummarizationPrompt(normalized))
        {
            classification = BuildSummarizationClassification(audienceMode, verbosityMode);
            return true;
        }

        if (IsCodeGenerationPrompt(normalized))
        {
            classification = BuildCodeGenerationClassification(audienceMode, verbosityMode);
            return true;
        }

        if (TryClassifyMemoryRecall(normalized, audienceMode, verbosityMode, out classification))
        {
            return true;
        }

        if (IsBroadCurrentEventsPrompt(normalized))
        {
            classification = BuildBroadCurrentEventsClassification(audienceMode);
            return true;
        }

        if (ToolOrCurrentInfoPromptPolicy.TryClassifySearchFallbackIntent(normalized, out var searchFallbackIntent))
        {
            classification = BuildToolIntentClassification(searchFallbackIntent);
            return true;
        }

        return NoClassification(out classification);
    }

    private static AskQueryClassification ClassifyDomainOrGeneral(
        string normalized,
        string intentType,
        string? audienceMode,
        string? verbosityMode,
        bool sourceSeeking)
    {
        var domainTerms = ExtractRetrievalDomainTerms(normalized);
        var hasRetrievalIntent = HasRetrievalIntent(normalized, intentType, domainTerms);
        if (!hasRetrievalIntent || domainTerms.Count == 0)
        {
            return BuildNoRetrievalClassification(intentType, audienceMode, verbosityMode, sourceSeeking);
        }

        return new AskQueryClassification(
            "domain_request",
            "Retrieval intent and domain context matched; source retrieval may run with domain filtering.",
            intentType,
            audienceMode,
            verbosityMode,
            RetrievalAllowed: true,
            GroundingMode: "source_grounded",
            RetrievalDomain: ResolveRetrievalDomain(domainTerms),
            RetrievalDomainTerms: domainTerms,
            RetrievalSkippedReason: null,
            RetrievalMinScore: DefaultRetrievalMinScore,
            EvidenceRequired: true,
            MemoryUpdateKind: null,
            MemoryUpdateValue: null);
    }

    private static AskQueryClassification BuildHomeNodeDefinitionClassification(
        string reason,
        string intentType,
        string? audienceMode,
        string? verbosityMode)
    {
        return new AskQueryClassification(
            "home_node_definition",
            reason,
            intentType,
            audienceMode,
            verbosityMode,
            RetrievalAllowed: false,
            GroundingMode: "system_truth",
            RetrievalDomain: "cwi_home_node",
            RetrievalDomainTerms: new[] { "cwi", "home", "node", "local", "offline", "wifi" },
            RetrievalSkippedReason: "system_truth_uses_governed_identity_pack",
            RetrievalMinScore: DefaultRetrievalMinScore,
            EvidenceRequired: false,
            MemoryUpdateKind: null,
            MemoryUpdateValue: null);
    }

    private static AskQueryClassification BuildIdentityDefinitionClassification(
        string reason,
        string intentType,
        string? audienceMode,
        string? verbosityMode)
    {
        return new AskQueryClassification(
            "identity_definition",
            reason,
            intentType,
            audienceMode,
            verbosityMode,
            RetrievalAllowed: false,
            GroundingMode: "system_truth",
            RetrievalDomain: "cwi_identity",
            RetrievalDomainTerms: new[] { "cwi", "christian", "wheelwright", "intelligence", "identity" },
            RetrievalSkippedReason: "system_truth_uses_governed_identity_pack",
            RetrievalMinScore: DefaultRetrievalMinScore,
            EvidenceRequired: false,
            MemoryUpdateKind: null,
            MemoryUpdateValue: null);
    }

    private static AskQueryClassification BuildGeneralNonRetrievalClassification(
        string reason,
        string intentType,
        string? audienceMode,
        string? verbosityMode,
        string retrievalSkippedReason)
    {
        return new AskQueryClassification(
            "general_reasoning",
            reason,
            intentType,
            audienceMode,
            verbosityMode,
            RetrievalAllowed: false,
            GroundingMode: "non_grounded",
            RetrievalDomain: null,
            RetrievalDomainTerms: Array.Empty<string>(),
            RetrievalSkippedReason: retrievalSkippedReason,
            RetrievalMinScore: DefaultRetrievalMinScore,
            EvidenceRequired: false,
            MemoryUpdateKind: null,
            MemoryUpdateValue: null);
    }

    private static AskQueryClassification BuildRiskModeClassification(string normalized, string? audienceMode)
    {
        var riskIntent = normalized.Contains("with this plan", StringComparison.Ordinal)
            || normalized.Contains("risk with", StringComparison.Ordinal)
            || normalized.Contains("at risk with", StringComparison.Ordinal)
                ? "risk_analysis"
                : "risk_review";
        return BuildProactiveClassification(
            riskIntent,
            "risks",
            new[] { "risk", "blocked" },
            "Current prompt asks for risk response mode; routing to proactive intelligence APIs.",
            audienceMode);
    }

    private static AskQueryClassification BuildPlanModeClassification(string normalized, string? audienceMode)
    {
        var planIntent = normalized.Contains("help me make a plan", StringComparison.Ordinal)
            || normalized.Contains("help me plan", StringComparison.Ordinal)
                ? "planning"
                : "plan_request";
        return BuildProactiveClassification(
            planIntent,
            "priorities",
            new[] { "priority", "focus", "next" },
            "Current prompt asks for planning mode; routing to proactive intelligence APIs.",
            audienceMode);
    }

    private static AskQueryClassification BuildCurrentEventsWebDisabledClassification(string? audienceMode)
    {
        return new AskQueryClassification(
            "current_events_web_disabled",
            "The prompt explicitly says web is off while asking for current information; do not run search.",
            "current_events",
            audienceMode,
            "concise",
            RetrievalAllowed: false,
            GroundingMode: "non_grounded",
            RetrievalDomain: null,
            RetrievalDomainTerms: Array.Empty<string>(),
            RetrievalSkippedReason: "web_disabled_for_current_info",
            RetrievalMinScore: DefaultRetrievalMinScore,
            EvidenceRequired: false,
            MemoryUpdateKind: null,
            MemoryUpdateValue: null);
    }

    private static AskQueryClassification BuildClarificationClassification(string? audienceMode, string? verbosityMode)
    {
        return new AskQueryClassification(
            "clarification_required",
            "Detected an action request without enough target detail; asking clarification instead of guessing.",
            "needs_clarification",
            audienceMode,
            verbosityMode,
            RetrievalAllowed: false,
            GroundingMode: "non_grounded",
            RetrievalDomain: null,
            RetrievalDomainTerms: Array.Empty<string>(),
            RetrievalSkippedReason: "ambiguous_action_requires_clarification",
            RetrievalMinScore: DefaultRetrievalMinScore,
            EvidenceRequired: false,
            MemoryUpdateKind: null,
            MemoryUpdateValue: null);
    }

    private static AskQueryClassification BuildSummarizationClassification(string? audienceMode, string? verbosityMode)
    {
        return new AskQueryClassification(
            "summarization",
            "Detected a summarization request; enforcing condensed summary response mode.",
            "summarization",
            audienceMode,
            verbosityMode ?? "concise",
            RetrievalAllowed: false,
            GroundingMode: "non_grounded",
            RetrievalDomain: null,
            RetrievalDomainTerms: Array.Empty<string>(),
            RetrievalSkippedReason: "summarization_uses_user_provided_text_or_model_reasoning",
            RetrievalMinScore: DefaultRetrievalMinScore,
            EvidenceRequired: false,
            MemoryUpdateKind: null,
            MemoryUpdateValue: null);
    }

    private static AskQueryClassification BuildCodeGenerationClassification(string? audienceMode, string? verbosityMode)
    {
        return new AskQueryClassification(
            "code_generation",
            "Detected a code-generation request; enforcing complete runnable code response mode.",
            "code_generation",
            audienceMode,
            verbosityMode,
            RetrievalAllowed: false,
            GroundingMode: "non_grounded",
            RetrievalDomain: null,
            RetrievalDomainTerms: Array.Empty<string>(),
            RetrievalSkippedReason: "code_generation_does_not_require_enterprise_retrieval",
            RetrievalMinScore: DefaultRetrievalMinScore,
            EvidenceRequired: false,
            MemoryUpdateKind: null,
            MemoryUpdateValue: null);
    }

    private static bool TryClassifyMemoryRecall(
        string normalized,
        string? audienceMode,
        string? verbosityMode,
        out AskQueryClassification classification)
    {
        if (!UserMemoryRecallPromptPolicy.TryClassify(normalized, out var memoryRecall))
        {
            return NoClassification(out classification);
        }

        classification = new AskQueryClassification(
            "memory_graph_request",
            "Current prompt asks for imported user memory; routing to governed graph retrieval.",
            "memory_recall",
            audienceMode,
            verbosityMode,
            RetrievalAllowed: true,
            GroundingMode: "source_grounded",
            RetrievalDomain: memoryRecall.RetrievalDomain,
            RetrievalDomainTerms: memoryRecall.RetrievalDomainTerms,
            RetrievalSkippedReason: null,
            RetrievalMinScore: DefaultRetrievalMinScore,
            EvidenceRequired: true,
            MemoryUpdateKind: null,
            MemoryUpdateValue: null);
        return true;
    }

    private static AskQueryClassification BuildBroadCurrentEventsClassification(string? audienceMode)
    {
        return new AskQueryClassification(
            "current_events",
            "Detected a broad current-events or topic prompt; routing outside enterprise/entity lookup.",
            "current_events",
            audienceMode,
            "concise",
            RetrievalAllowed: false,
            GroundingMode: "non_grounded",
            RetrievalDomain: null,
            RetrievalDomainTerms: Array.Empty<string>(),
            RetrievalSkippedReason: "current_events_without_live_connector",
            RetrievalMinScore: DefaultRetrievalMinScore,
            EvidenceRequired: false,
            MemoryUpdateKind: null,
            MemoryUpdateValue: null);
    }

    private static AskQueryClassification BuildNoRetrievalClassification(
        string intentType,
        string? audienceMode,
        string? verbosityMode,
        bool sourceSeeking)
    {
        var classification = intentType switch
        {
            "troubleshooting" => "troubleshooting",
            "explanation" when !sourceSeeking => "general_model_knowledge",
            _ => "general_reasoning"
        };
        return new AskQueryClassification(
            classification,
            ResolveNoRetrievalReason(classification, sourceSeeking),
            intentType,
            audienceMode,
            verbosityMode,
            RetrievalAllowed: false,
            GroundingMode: sourceSeeking ? "source_grounded" : "non_grounded",
            RetrievalDomain: null,
            RetrievalDomainTerms: Array.Empty<string>(),
            RetrievalSkippedReason: ResolveNoRetrievalSkippedReason(classification, intentType, sourceSeeking),
            RetrievalMinScore: DefaultRetrievalMinScore,
            EvidenceRequired: sourceSeeking,
            MemoryUpdateKind: null,
            MemoryUpdateValue: null);
    }

    private static string ResolveNoRetrievalReason(string classification, bool sourceSeeking)
    {
        if (sourceSeeking)
        {
            return "Evidence was requested, but no specific retrieval domain matched; blocking source retrieval.";
        }

        return classification == "general_model_knowledge"
            ? "General knowledge question does not require enterprise source retrieval."
            : "No explicit retrieval intent plus domain context matched; answering without enterprise source retrieval.";
    }

    private static string ResolveNoRetrievalSkippedReason(
        string classification,
        string intentType,
        bool sourceSeeking)
    {
        if (sourceSeeking)
        {
            return "missing_retrieval_domain_context";
        }

        if (intentType is "troubleshooting")
        {
            return "troubleshooting_without_explicit_source_request";
        }

        return classification == "general_model_knowledge"
            ? "general_knowledge_does_not_require_enterprise_retrieval"
            : "missing_retrieval_intent_or_domain_context";
    }

    private static bool NoClassification(out AskQueryClassification classification)
    {
        classification = null!;
        return false;
    }

    private static string DetectIntentType(string normalizedQuestion)
    {
        if (IsMessageTransformationMissingInputPrompt(normalizedQuestion))
        {
            return "needs_clarification";
        }

        if (IsMessageDraftPrompt(normalizedQuestion))
        {
            return "message_draft";
        }

        if (IsFoodSafetyPrompt(normalizedQuestion))
        {
            return "food_safety";
        }

        if (IsEverydayPracticalGuidancePrompt(normalizedQuestion))
        {
            return "practical_guidance";
        }

        if (IsDailyBriefingPrompt(normalizedQuestion))
        {
            return "daily_briefing";
        }

        if (IsRiskModePrompt(normalizedQuestion))
        {
            if (normalizedQuestion.Contains("with this plan", StringComparison.Ordinal)
                || normalizedQuestion.Contains("risk with", StringComparison.Ordinal)
                || normalizedQuestion.Contains("at risk with", StringComparison.Ordinal))
            {
                return "risk_analysis";
            }

            return "risk_review";
        }

        if (IsChangeReviewPrompt(normalizedQuestion))
        {
            return "change_review";
        }

        if (IsPlanModePrompt(normalizedQuestion))
        {
            if (normalizedQuestion.Contains("help me make a plan", StringComparison.Ordinal)
                || normalizedQuestion.Contains("help me plan", StringComparison.Ordinal))
            {
                return "planning";
            }

            return "plan_request";
        }

        if (ToolOrCurrentInfoPromptPolicy.TryClassifyDirectToolIntent(normalizedQuestion, out var directToolIntent))
        {
            return directToolIntent.IntentType;
        }

        if (IsAmbiguousActionPrompt(normalizedQuestion))
        {
            return "needs_clarification";
        }

        if (IsPlainSummarizationPrompt(normalizedQuestion))
        {
            return "summarization";
        }

        if (IsCodeGenerationPrompt(normalizedQuestion))
        {
            return "code_generation";
        }

        if (IsBroadCurrentEventsPrompt(normalizedQuestion))
        {
            return "current_events";
        }

        if (ToolOrCurrentInfoPromptPolicy.TryClassifySearchFallbackIntent(normalizedQuestion, out var searchFallbackIntent))
        {
            return searchFallbackIntent.IntentType;
        }

        if (IsExecutiveSummaryPrompt(normalizedQuestion))
        {
            return "decision_support";
        }

        if (IsTroubleshootingPrompt(normalizedQuestion))
        {
            return "troubleshooting";
        }

        if (ContainsAny(normalizedQuestion, DecisionSupportPatterns))
        {
            return "decision_support";
        }

        if (ContainsAny(normalizedQuestion, OperationalPatterns))
        {
            return "operational";
        }

        if (ContainsAny(normalizedQuestion, ExplanationPatterns))
        {
            return "explanation";
        }

        return "explanation";
    }

    private static bool IsExecutiveSummaryPrompt(string normalizedQuestion)
    {
        var asksForSummary = normalizedQuestion.Contains("summarize", StringComparison.Ordinal)
            || normalizedQuestion.Contains("summary", StringComparison.Ordinal)
            || normalizedQuestion.Contains("executive brief", StringComparison.Ordinal)
            || normalizedQuestion.Contains("executive summary", StringComparison.Ordinal);
        if (!asksForSummary)
        {
            return false;
        }

        return normalizedQuestion.Contains("impact", StringComparison.Ordinal)
            || normalizedQuestion.Contains("system", StringComparison.Ordinal)
            || normalizedQuestion.Contains("board", StringComparison.Ordinal)
            || normalizedQuestion.Contains("investor", StringComparison.Ordinal)
            || normalizedQuestion.Contains("demo", StringComparison.Ordinal)
            || normalizedQuestion.Contains("leadership", StringComparison.Ordinal);
    }

    private static string? DetectAudienceMode(string normalizedQuestion)
    {
        var isHomeUser = normalizedQuestion.Contains("home user", StringComparison.Ordinal)
            || normalizedQuestion.Contains("homeowner", StringComparison.Ordinal)
            || normalizedQuestion.Contains("for a user", StringComparison.Ordinal);
        var isEngineer = normalizedQuestion.Contains("engineer", StringComparison.Ordinal)
            || normalizedQuestion.Contains("developer", StringComparison.Ordinal)
            || normalizedQuestion.Contains("technical", StringComparison.Ordinal);
        var isExecutive = normalizedQuestion.Contains("executive", StringComparison.Ordinal)
            || normalizedQuestion.Contains("leader", StringComparison.Ordinal)
            || normalizedQuestion.Contains("business impact", StringComparison.Ordinal)
            || normalizedQuestion.Contains("investor", StringComparison.Ordinal)
            || normalizedQuestion.Contains("investors", StringComparison.Ordinal);

        var matches = 0;
        matches += isHomeUser ? 1 : 0;
        matches += isEngineer ? 1 : 0;
        matches += isExecutive ? 1 : 0;
        if (matches > 1)
        {
            return null;
        }

        if (isHomeUser)
        {
            return "home_user";
        }

        if (isEngineer)
        {
            return "engineer";
        }

        if (isExecutive)
        {
            return "executive";
        }

        return null;
    }

    private static string? DetectVerbosityMode(string normalizedQuestion)
    {
        if (normalizedQuestion.Contains("brief", StringComparison.Ordinal)
            || normalizedQuestion.Contains("concise", StringComparison.Ordinal)
            || normalizedQuestion.Contains("short", StringComparison.Ordinal))
        {
            return "concise";
        }

        if (normalizedQuestion.Contains("step by step", StringComparison.Ordinal)
            || normalizedQuestion.Contains("stepwise", StringComparison.Ordinal))
        {
            return "step_by_step";
        }

        if (normalizedQuestion.Contains("detailed", StringComparison.Ordinal)
            || normalizedQuestion.Contains("in depth", StringComparison.Ordinal)
            || normalizedQuestion.StartsWith("explain ", StringComparison.Ordinal))
        {
            return "detailed";
        }

        return null;
    }

    private static bool IsPreferenceUpdate(string normalizedQuestion)
    {
        return ContainsAny(normalizedQuestion, PreferencePatterns);
    }

    private static bool TryClassifyControlledFact(
        string primaryQuestion,
        string normalizedQuestion,
        string? audienceMode,
        string? verbosityMode,
        out AskQueryClassification classification)
    {
        classification = default!;
        var entityTerms = ExtractControlledFactEntityTerms(primaryQuestion);
        if (entityTerms.Count == 0)
        {
            return false;
        }

        var terms = ExtractControlledFactTerms(primaryQuestion, entityTerms);
        if (IsControlledFactLookup(primaryQuestion, normalizedQuestion))
        {
            classification = new AskQueryClassification(
                "controlled_fact_lookup",
                "Detected a controlled enterprise fact lookup; routing to governed memory retrieval.",
                "memory_recall",
                audienceMode,
                verbosityMode,
                RetrievalAllowed: true,
                GroundingMode: "source_grounded",
                RetrievalDomain: "controlled_fact",
                RetrievalDomainTerms: terms,
                RetrievalSkippedReason: null,
                RetrievalMinScore: DefaultRetrievalMinScore,
                EvidenceRequired: true,
                MemoryUpdateKind: null,
                MemoryUpdateValue: null);
            return true;
        }

        if (!IsControlledFactStatement(primaryQuestion, normalizedQuestion))
        {
            return false;
        }

        classification = new AskQueryClassification(
            "controlled_fact_update",
            "Detected a controlled enterprise fact statement; routing to governed memory update.",
            "memory_update",
            audienceMode,
            verbosityMode,
            RetrievalAllowed: false,
            GroundingMode: "memory_update",
            RetrievalDomain: "controlled_fact",
            RetrievalDomainTerms: terms,
            RetrievalSkippedReason: "controlled_fact_update_routes_to_memory",
            RetrievalMinScore: DefaultRetrievalMinScore,
            EvidenceRequired: false,
            MemoryUpdateKind: "controlled_fact",
            MemoryUpdateValue: primaryQuestion.Trim());
        return true;
    }

    private static bool IsControlledFactLookup(string primaryQuestion, string normalizedQuestion)
    {
        return primaryQuestion.Contains('?', StringComparison.Ordinal)
               || StartsWithQuestionWord(normalizedQuestion)
               || IsControlledFactLookupCommand(normalizedQuestion)
               || normalizedQuestion.StartsWith("use current evidence", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("use latest evidence", StringComparison.Ordinal);
    }

    private static bool IsControlledFactLookupCommand(string normalizedQuestion)
    {
        return normalizedQuestion.StartsWith("summarize ", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("summarise ", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("answer ", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("give ", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("state ", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("tell me ", StringComparison.Ordinal);
    }

    private static bool IsControlledFactStatement(string primaryQuestion, string normalizedQuestion)
    {
        if (string.IsNullOrWhiteSpace(primaryQuestion)
            || primaryQuestion.Contains('?', StringComparison.Ordinal)
            || StartsWithQuestionWord(normalizedQuestion)
            || ContainsConversationMarker(normalizedQuestion))
        {
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> ExtractControlledFactEntityTerms(string question)
    {
        return ControlledFactEntityRegex.Matches(question)
            .Select(match => NormalizeControlledFactEntity(match.Value))
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ExtractControlledFactTerms(
        string question,
        IReadOnlyList<string> entityTerms)
    {
        var terms = new List<string>();
        terms.AddRange(entityTerms);
        foreach (Match match in ControlledFactTermRegex.Matches(question))
        {
            var token = match.Value.Trim();
            if (token.Length < 3 || DomainStopWords.Contains(token))
            {
                continue;
            }

            terms.Add(token);
        }

        terms.Add("controlled_fact");
        return terms
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    private static string NormalizeControlledFactEntity(string value)
    {
        return Regex.Replace(value.Trim(), @"\s+", "-", RegexOptions.CultureInvariant)
            .ToUpperInvariant();
    }

    private static bool IsRiskModePrompt(string normalizedQuestion)
    {
        return ContainsAny(normalizedQuestion, RiskModePatterns);
    }

    private static bool IsDailyBriefingPrompt(string normalizedQuestion)
    {
        return ContainsAny(normalizedQuestion, DailyBriefingPatterns);
    }

    private static bool IsChangeReviewPrompt(string normalizedQuestion)
    {
        return ContainsAny(normalizedQuestion, ChangeReviewPatterns)
               || normalizedQuestion is "what is new" or "whats new" or "what's new"
               || normalizedQuestion.StartsWith("what is new today", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("whats new today", StringComparison.Ordinal);
    }

    private static bool IsPlanModePrompt(string normalizedQuestion)
    {
        return ContainsAny(normalizedQuestion, PlanModePatterns);
    }

    private static bool IsBroadCurrentEventsPrompt(string normalizedQuestion)
    {
        if (ContainsAny(normalizedQuestion, CurrentEventsPatterns))
        {
            return true;
        }

        return (normalizedQuestion.Contains("world", StringComparison.Ordinal)
                || normalizedQuestion.Contains("global", StringComparison.Ordinal))
               && (normalizedQuestion.Contains("topics", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("trends", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("headlines", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("events", StringComparison.Ordinal));
    }

    private static bool IsPlainSummarizationPrompt(string normalizedQuestion)
    {
        return !IsCwiExecutiveSummaryRequest(normalizedQuestion)
               && !IsExecutiveSummaryPrompt(normalizedQuestion)
               && ContainsAny(normalizedQuestion, SummarizationPatterns);
    }

    private static bool IsCodeGenerationPrompt(string normalizedQuestion)
    {
        return ContainsAny(normalizedQuestion, CodeGenerationPatterns)
               && (normalizedQuestion.Contains("code", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("function", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("script", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("program", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("python", StringComparison.Ordinal));
    }

    private static bool IsMessageDraftPrompt(string normalizedQuestion)
    {
        return MessageDraftPromptPatterns.Any(pattern => pattern.IsMatch(normalizedQuestion));
    }

    private static bool IsMessageTransformationMissingInputPrompt(string normalizedQuestion)
    {
        var asksToTransformMissingText =
            normalizedQuestion.Contains("messy explanation", StringComparison.Ordinal)
            && normalizedQuestion.Contains("short message", StringComparison.Ordinal);
        var asksForSendableMessageWithoutContent =
            normalizedQuestion.Contains("sendable message first", StringComparison.Ordinal)
            || normalizedQuestion.Contains("sendable text first", StringComparison.Ordinal)
            || normalizedQuestion.Contains("sendable note first", StringComparison.Ordinal)
            || normalizedQuestion.Contains("sendable reply first", StringComparison.Ordinal);

        return (asksToTransformMissingText || asksForSendableMessageWithoutContent)
               && !normalizedQuestion.Contains(":", StringComparison.Ordinal)
               && !normalizedQuestion.Contains("\"", StringComparison.Ordinal);
    }

    private static bool IsFoodSafetyPrompt(string normalizedQuestion)
    {
        return ContainsAny(normalizedQuestion, FoodSafetyPatterns);
    }

    private static bool IsEverydayPracticalGuidancePrompt(string normalizedQuestion)
    {
        if (ContainsDomainSignal(normalizedQuestion))
        {
            return false;
        }

        return ContainsAny(normalizedQuestion, EverydayLifeContextPatterns)
               && ContainsAny(normalizedQuestion, PracticalGuidanceCuePatterns);
    }

    private static bool IsTroubleshootingPrompt(string normalizedQuestion)
    {
        if (!ContainsAny(normalizedQuestion, TroubleshootingPatterns))
        {
            return false;
        }

        if (IsEverydayPracticalGuidancePrompt(normalizedQuestion))
        {
            return false;
        }

        return true;
    }

    private static bool IsAmbiguousActionPrompt(string normalizedQuestion)
    {
        return Regex.IsMatch(
            normalizedQuestion,
            @"^(call|text|message|email|send|delete|remove)\s+[a-z][a-z0-9\-]*(?:\s+[a-z][a-z0-9\-]*){0,3}\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsCurrentInfoPromptWithWebDisabled(string normalizedQuestion)
    {
        return ToolOrCurrentInfoPromptPolicy.HasWebOffDirective(normalizedQuestion)
               && (ToolOrCurrentInfoPromptPolicy.HasFreshnessCue(normalizedQuestion)
                   || normalizedQuestion.Contains("latest", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("right now", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("current", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("news", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("score", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("rockets", StringComparison.Ordinal));
    }

    private static bool IsCwiExecutiveSummaryRequest(string normalizedQuestion)
    {
        return normalizedQuestion.Contains("executive summary", StringComparison.Ordinal)
               && normalizedQuestion.Contains("cwi", StringComparison.Ordinal)
               && (normalizedQuestion.Contains("does", StringComparison.Ordinal)
                   || normalizedQuestion.Contains("is", StringComparison.Ordinal));
    }

    private static AskQueryClassification BuildProactiveClassification(
        string intentType,
        string retrievalDomain,
        IReadOnlyList<string> domainTerms,
        string reason,
        string? audienceMode)
    {
        return new AskQueryClassification(
            "proactive_intelligence_request",
            reason,
            intentType,
            audienceMode,
            "concise",
            RetrievalAllowed: false,
            GroundingMode: "proactive_intelligence",
            RetrievalDomain: retrievalDomain,
            RetrievalDomainTerms: domainTerms,
            RetrievalSkippedReason: "proactive_intelligence_api_route",
            RetrievalMinScore: DefaultRetrievalMinScore,
            EvidenceRequired: false,
            MemoryUpdateKind: null,
            MemoryUpdateValue: null);
    }

    private static AskQueryClassification BuildToolIntentClassification(
        string intentType,
        string retrievalDomain,
        IReadOnlyList<string> domainTerms,
        string reason)
    {
        return new AskQueryClassification(
            "tool_intent_request",
            reason,
            intentType,
            AudienceMode: null,
            VerbosityMode: "concise",
            RetrievalAllowed: false,
            GroundingMode: "tool_intent",
            RetrievalDomain: retrievalDomain,
            RetrievalDomainTerms: domainTerms,
            RetrievalSkippedReason: "mcp_execution_requires_p0_activation_gate",
            RetrievalMinScore: DefaultRetrievalMinScore,
            EvidenceRequired: false,
            MemoryUpdateKind: null,
            MemoryUpdateValue: null);
    }

    private static AskQueryClassification BuildToolIntentClassification(ToolOrCurrentInfoPromptMatch match)
    {
        return BuildToolIntentClassification(
            match.IntentType,
            match.RetrievalDomain,
            match.RetrievalDomainTerms,
            match.Reason);
    }

    private static string DetectPreferenceValue(string? question, string normalizedQuestion)
    {
        if (normalizedQuestion.Contains("step by step", StringComparison.Ordinal)
            || (question?.Contains("step-by-step", StringComparison.OrdinalIgnoreCase) ?? false)
            || normalizedQuestion.Contains("stepwise", StringComparison.Ordinal))
        {
            return "step_by_step";
        }

        if (normalizedQuestion.Contains("brief", StringComparison.Ordinal)
            || normalizedQuestion.Contains("concise", StringComparison.Ordinal)
            || normalizedQuestion.Contains("short", StringComparison.Ordinal))
        {
            return "concise";
        }

        if (normalizedQuestion.Contains("detailed", StringComparison.Ordinal)
            || normalizedQuestion.Contains("in depth", StringComparison.Ordinal))
        {
            return "detailed";
        }

        return "custom";
    }

    private static bool IsSimpleArithmetic(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return false;
        }

        var normalized = NormalizeArithmetic(question);
        return Regex.IsMatch(
            normalized,
            @"^(?:what\s+is|calculate|compute|solve)?\s*[-+]?\d+(?:\.\d+)?\s*(?:\+|-|\*|/|x)\s*[-+]?\d+(?:\.\d+)?\s*\??$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string NormalizeArithmetic(string value)
    {
        var normalized = LanguageNormalizer.NormalizeForRouting(value);
        normalized = normalized
            .Replace("what is the sum of ", "what is ", StringComparison.Ordinal)
            .Replace("what's the sum of ", "what is ", StringComparison.Ordinal)
            .Replace("calculate the sum of ", "calculate ", StringComparison.Ordinal)
            .Replace("compute the sum of ", "compute ", StringComparison.Ordinal)
            .Replace("solve the sum of ", "solve ", StringComparison.Ordinal);

        return normalized
            .Replace(" plus ", " + ", StringComparison.Ordinal)
            .Replace(" minus ", " - ", StringComparison.Ordinal)
            .Replace(" times ", " * ", StringComparison.Ordinal)
            .Replace(" multiplied by ", " * ", StringComparison.Ordinal)
            .Replace(" divided by ", " / ", StringComparison.Ordinal)
            .Replace(" over ", " / ", StringComparison.Ordinal);
    }

    private static bool HasRetrievalIntent(
        string normalizedQuestion,
        string intentType,
        IReadOnlyList<string> domainTerms)
    {
        if (domainTerms.Count == 0)
        {
            return false;
        }

        if (ContainsAny(normalizedQuestion, SourceSeekingPatterns))
        {
            return true;
        }

        return false;
    }

    private static IReadOnlyList<string> ExtractRetrievalDomainTerms(string normalizedQuestion)
    {
        var matchedSignals = DomainSignalPatterns
            .Where(pattern => normalizedQuestion.Contains(pattern, StringComparison.Ordinal))
            .SelectMany(Tokenize)
            .Where(token => !DomainStopWords.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matchedSignals.Count == 0)
        {
            return Array.Empty<string>();
        }

        if (!ContainsAny(normalizedQuestion, SourceSeekingPatterns))
        {
            return matchedSignals.Take(8).ToList();
        }

        return matchedSignals.Take(8).ToList();
    }

    private static string ResolveRetrievalDomain(IReadOnlyList<string> domainTerms)
    {
        if (domainTerms.Any(term => term is "docker" or "container" or "curl"))
        {
            return "container_operations";
        }

        if (domainTerms.Any(term => term is "graph" or "tooling" or "qdrant" or "vector"))
        {
            return "graph_retrieval";
        }

        if (domainTerms.Any(term => term is "iteration" or "cutover"))
        {
            return "deployment_cutover";
        }

        if (domainTerms.Any(term => term is "home" or "node" or "wifi" or "offline" or "bluetooth"))
        {
            return "cwi_home_node";
        }

        return "domain_context";
    }

    private static bool ContainsAny(string value, IReadOnlyList<string> patterns)
    {
        return patterns.Any(pattern => value.Contains(pattern, StringComparison.Ordinal));
    }

    private static bool ContainsDomainSignal(string value)
    {
        return DomainSignalPatterns.Any(pattern =>
            Regex.IsMatch(
                value,
                $@"(?<![a-z0-9]){Regex.Escape(pattern)}(?![a-z0-9])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    private static bool IsDirectIdentityQuestion(string normalizedQuestion)
    {
        if (!normalizedQuestion.Contains("cwi", StringComparison.Ordinal))
        {
            return false;
        }

        if (ContainsAny(normalizedQuestion, TroubleshootingPatterns)
            || IsExecutiveSummaryPrompt(normalizedQuestion)
            || ContainsConversationMarker(normalizedQuestion))
        {
            return false;
        }

        var directIdentityPhrases = new[]
        {
            "what is cwi",
            "define cwi",
            "what does cwi stand for",
            "what does cwi do",
            "explain cwi",
            "can you explain cwi",
            "tell me about cwi",
            "describe cwi"
        };

        return directIdentityPhrases.Any(phrase =>
            normalizedQuestion.Equals(phrase, StringComparison.Ordinal)
            || normalizedQuestion.StartsWith(phrase + " ", StringComparison.Ordinal)
            || normalizedQuestion.EndsWith(" " + phrase, StringComparison.Ordinal));
    }

    private static bool IsCwiMemoryActivationQuestion(string normalizedQuestion)
    {
        if (!normalizedQuestion.Contains("cwi", StringComparison.Ordinal)
            || !normalizedQuestion.Contains("memory", StringComparison.Ordinal))
        {
            return false;
        }

        if (ContainsConversationMarker(normalizedQuestion))
        {
            return false;
        }

        return normalizedQuestion.Contains("memory activation", StringComparison.Ordinal)
               || (normalizedQuestion.Contains("memory", StringComparison.Ordinal)
                   && normalizedQuestion.Contains("activation", StringComparison.Ordinal)
                   && ContainsAny(normalizedQuestion, new[] { "explain", "works", "working", "how" }));
    }

    private static bool ContainsConversationMarker(string normalizedQuestion)
    {
        return normalizedQuestion.StartsWith("user ", StringComparison.Ordinal)
               || normalizedQuestion.Contains(" assistant ", StringComparison.Ordinal)
               || normalizedQuestion.Contains(" conversation context ", StringComparison.Ordinal)
               || normalizedQuestion.Contains(" recent conversation ", StringComparison.Ordinal)
               || normalizedQuestion.Contains(" chat history ", StringComparison.Ordinal)
               || normalizedQuestion.Contains(" primary question ", StringComparison.Ordinal)
               || normalizedQuestion.Contains(" current question ", StringComparison.Ordinal);
    }

    private static bool IsSelfContextDisclosure(string primaryQuestion, string normalizedQuestion)
    {
        if (string.IsNullOrWhiteSpace(normalizedQuestion)
            || primaryQuestion.Contains('?', StringComparison.Ordinal)
            || StartsWithQuestionWord(normalizedQuestion)
            || ContainsConversationMarker(normalizedQuestion))
        {
            return false;
        }

        return IsIdentityDisclosure(normalizedQuestion)
               || IsWorkRoleDisclosure(normalizedQuestion);
    }

    private static bool IsIdentityDisclosure(string normalizedQuestion)
    {
        return Regex.IsMatch(
            normalizedQuestion,
            @"\b(?:my name is|i am named|im named|i'm named|call me)\s+[a-z]",
            RegexOptions.CultureInvariant);
    }

    private static bool IsWorkRoleDisclosure(string normalizedQuestion)
    {
        return Regex.IsMatch(
            normalizedQuestion,
            @"\b(?:i work as|i am|im|i'm|my job is|my profession is|my role is)\s+(?:a|an|the)?\s*(?:[a-z'-]+\s+){0,4}(?:engineer|developer|architect|designer|analyst|manager|consultant|administrator|technician|programmer|operator|director|specialist)\b",
            RegexOptions.CultureInvariant);
    }

    private static bool StartsWithQuestionWord(string normalizedQuestion)
    {
        return normalizedQuestion.StartsWith("what ", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("who ", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("when ", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("where ", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("why ", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("how ", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("is ", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("are ", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("which ", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("do ", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("does ", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("can ", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("could ", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("should ", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ExtractSelfContextTerms(string normalizedQuestion)
    {
        var terms = new List<string>();
        if (IsIdentityDisclosure(normalizedQuestion))
        {
            terms.Add("identity");
            terms.Add("name");
        }

        if (IsWorkRoleDisclosure(normalizedQuestion))
        {
            terms.Add("work");
            terms.Add("role");
        }

        return terms.Count == 0
            ? new[] { "user", "context" }
            : terms.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsDirectHomeNodeQuestion(string normalizedQuestion)
    {
        if (!normalizedQuestion.Contains("home node", StringComparison.Ordinal))
        {
            return false;
        }

        return ContainsAny(normalizedQuestion, HomeNodePatterns)
               || normalizedQuestion.StartsWith("explain home node", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("explain the home node", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("explain how the home node", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("tell me about home node", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("tell me about the home node", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("describe home node", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("describe the home node", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("what does the home node", StringComparison.Ordinal)
               || normalizedQuestion.StartsWith("what does a home node", StringComparison.Ordinal);
    }

    private static string Normalize(string? value)
    {
        return LanguageNormalizer.NormalizeForRouting(value);
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                continue;
            }

            foreach (var token in FlushToken(builder))
            {
                yield return token;
            }
        }

        foreach (var token in FlushToken(builder))
        {
            yield return token;
        }
    }

    private static IEnumerable<string> FlushToken(StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            yield break;
        }

        var token = builder.ToString();
        builder.Clear();
        if (token.Length >= 3)
        {
            yield return token;
        }
    }
}
#nullable disable
