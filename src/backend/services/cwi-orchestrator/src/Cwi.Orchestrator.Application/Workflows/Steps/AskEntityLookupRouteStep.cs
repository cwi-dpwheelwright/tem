#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;
using Cwi.Common;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Retrieval;
using Cwi.Orchestrator.Application.Trace;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class AskEntityLookupRouteStep : IAskWorkflowStep
{
    private static readonly Regex ControlledFactEntityRegex = new(
        @"\bcwi[-\s]?rl[-\s]?\d{3,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex WhoIsPattern = new(
        @"^\s*who\s+(?:is|are)\s+(?<entity>.+?)\s*\??\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex DoYouKnowPattern = new(
        @"^\s*do\s+you\s+know\s+(?<entity>.+?)\s*\??\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TellMeAboutPattern = new(
        @"^\s*(?:tell\s+me\s+about|what\s+can\s+you\s+tell\s+me\s+about)\s+(?<entity>.+?)\s*\??\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex WhatIsPattern = new(
        @"^\s*what\s+(?:is|are)\s+(?<entity>.+?)\s*\??\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex WhatCompanyIsPattern = new(
        @"^\s*what\s+company\s+(?:is|are)\s+(?<entity>.+?)\s*\??\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex WhatPublicFactPattern = new(
        @"^\s*(?:what\s+(?:is|are)|whats|what's)\s+(?:the\s+)?(?<attribute>capital|population|currency|languages?|location|area|size|gdp|gross\s+domestic\s+product|time\s*zone|largest\s+city|major\s+cities|surrounding\s+cities|nearby\s+cities|state\s+bird|state\s+flower|median\s+income|unemployment\s+rate)\s+(?:of|in|for|near|around)\s+.+?\s*\??\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex HowManyPeoplePattern = new(
        @"^\s*how\s+many\s+people\s+(?:live|are)\s+(?:in|within|across)\s+.+?\s*\??\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PublicOfficeFactPattern = new(
        @"^\s*who\s+(?:is|are)\s+(?:the\s+)?(?:governor|mayor|president|prime\s+minister|senator|representative|secretary|capital|leader)\s+(?:of|in|for)\s+.+?\s*\??\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AdviceOrMethodPromptPattern = new(
        @"^\s*(?:(?:what\s+(?:is|are)|what's|whats)\s+(?:a|the)\s+(?:good|best|better|simple|easy|quick|safe|cheap|low-cost|practical)\s+(?:way|ways|method|approach|plan|checklist|routine|list|option|options)\b|how\s+do\s+i\s+.+)\s*.*\??\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex CwiBuilderPattern = new(
        @"^\s*who\s+(?:built|made|created)\s+(?:the\s+)?(?:cwi|christian\s+wheelwright\s+intelligence)\s*\??\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, PublicEntityAnswer> KnownPublicEntities =
        new Dictionary<string, PublicEntityAnswer>(StringComparer.OrdinalIgnoreCase)
        {
            ["elon musk"] = new(
                "Elon Musk is a public entrepreneur and executive known for work with companies including Tesla, SpaceX, and X.",
                "public_entity_registry"),
            ["sam altman"] = new(
                "Sam Altman is a public technology executive and entrepreneur best known as the CEO of OpenAI.",
                "public_entity_registry"),
            ["sam altamn openai"] = new(
                "Sam Altman is a public technology executive and entrepreneur best known as the CEO of OpenAI.",
                "public_entity_registry")
        };

    private static readonly HashSet<string> GenericSingleWordEntities = new(StringComparer.OrdinalIgnoreCase)
    {
        "ai",
        "api",
        "application",
        "business",
        "computer",
        "date",
        "docker",
        "engine",
        "graph",
        "memory",
        "node",
        "orchestrator",
        "policy",
        "router",
        "system",
        "time",
        "water"
    };

    private static readonly HashSet<string> EntityNoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "an",
        "the",
        "this",
        "that",
        "these",
        "those",
        "your",
        "our",
        "my",
        "their",
        "current",
        "today",
        "tomorrow",
        "yesterday"
    };

    private static readonly HashSet<string> BroadTopicWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "current",
        "event",
        "events",
        "global",
        "headline",
        "headlines",
        "issue",
        "issues",
        "latest",
        "news",
        "people",
        "public",
        "stuff",
        "thing",
        "things",
        "topic",
        "topics",
        "trend",
        "trends",
        "world",
        "worlds"
    };

    private static readonly HashSet<string> PublicFactAttributeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "area",
        "bird",
        "capital",
        "cities",
        "city",
        "currency",
        "flower",
        "gdp",
        "income",
        "language",
        "languages",
        "location",
        "major",
        "nearby",
        "people",
        "population",
        "rate",
        "size",
        "surrounding",
        "timezone",
        "unemployment",
        "zone"
    };

    private static readonly HashSet<string> PublicFactPrepositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "about",
        "across",
        "around",
        "for",
        "in",
        "near",
        "of",
        "within"
    };

    private static readonly string[] GeneralKnowledgeEntityPhrases =
    {
        "capital of",
        "population of",
        "currency of",
        "language of",
        "location of",
        "definition of",
        "meaning of",
        "history of"
    };

    private static readonly string[] ProactiveCommandPatterns =
    {
        "what is at risk",
        "what's at risk",
        "at risk right now",
        "risk right now",
        "risks right now",
        "what is the risk",
        "what are the risks",
        "daily briefing",
        "today's briefing",
        "todays briefing",
        "morning briefing",
        "brief me",
        "what changed",
        "what's changed",
        "what has changed",
        "what changed today",
        "recent changes",
        "make a plan",
        "build a plan",
        "create a plan",
        "plan my day",
        "help me plan"
    };

    private readonly GovernedMemoryService _memoryService;
    private readonly JourneyRequestContext _journeyRequestContext;
    private readonly TraceLookupStore _traceStore;
    private readonly ResponseIntelligenceOverlayService _responseIntelligenceOverlay;

    public AskEntityLookupRouteStep(
        GovernedMemoryService memoryService,
        JourneyRequestContext journeyRequestContext,
        TraceLookupStore traceStore,
        ResponseIntelligenceOverlayService? responseIntelligenceOverlay = null)
    {
        _memoryService = memoryService;
        _journeyRequestContext = journeyRequestContext;
        _traceStore = traceStore;
        _responseIntelligenceOverlay = responseIntelligenceOverlay ?? new ResponseIntelligenceOverlayService();
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (!TryExtractEntityLookup(context.RoutingQuestion, out var lookup))
        {
            return await next().ConfigureAwait(false);
        }

        if (IsHomeNodeEntity(lookup.Entity))
        {
            return await next().ConfigureAwait(false);
        }

        if (IsCwiEntity(lookup.Entity))
        {
            ApplySystemTruthAnswer(context, lookup.Entity);
            return CreateResponse(context);
        }

        if (TryResolveKnownPublicEntity(lookup.Entity, out var publicEntity))
        {
            ApplyPublicEntityAnswer(context, lookup.Entity, publicEntity);
            return CreateResponse(context);
        }

        var memoryLookup = await TryLookupUserEntityAsync(context, lookup.Entity, cancellationToken).ConfigureAwait(false);
        if (memoryLookup is not null)
        {
            ApplyUserEntityAnswer(context, lookup.Entity, memoryLookup);
            return CreateResponse(context);
        }

        ApplyUnknownEntityClarification(context, lookup.Entity);
        return CreateResponse(context);
    }

    private ServiceResponse<AskResponseDto> CreateResponse(AskWorkflowContext context)
    {
        _responseIntelligenceOverlay.Apply(context);
        return ServiceResponse<AskResponseDto>.Success(AskResponseFactory.Create(context, _journeyRequestContext, _traceStore));
    }

    public static bool TryExtractEntityLookup(string? question, out EntityLookup lookup)
    {
        lookup = default;
        var originalPrimaryQuestion = AskQuestionScope.ExtractPrimaryQuestion(question);
        var primaryQuestion = LanguageNormalizer.NormalizeForRouting(originalPrimaryQuestion);
        if (string.IsNullOrWhiteSpace(primaryQuestion))
        {
            return false;
        }

        if (ControlledFactEntityRegex.IsMatch(originalPrimaryQuestion)
            || ControlledFactEntityRegex.IsMatch(primaryQuestion))
        {
            return false;
        }

        if (LooksLikeUserMemoryRecallPrompt(primaryQuestion))
        {
            return false;
        }

        if (LooksLikeBroadTopicPrompt(primaryQuestion))
        {
            return false;
        }

        if (LooksLikePublicFactPrompt(primaryQuestion))
        {
            return false;
        }

        if (LooksLikeAdviceOrMethodPrompt(primaryQuestion))
        {
            return false;
        }

        if (LooksLikeProactiveCommandPrompt(primaryQuestion))
        {
            return false;
        }

        if (ToolOrCurrentInfoPromptPolicy.IsRouteProtectionMatch(primaryQuestion))
        {
            return false;
        }

        if (CwiBuilderPattern.IsMatch(primaryQuestion))
        {
            lookup = new EntityLookup("CWI", "system_truth");
            return true;
        }

        if (TryMatch(primaryQuestion, originalPrimaryQuestion, WhoIsPattern, requireNamedEntity: false, allowSingleTokenNamedEntity: true, out lookup)
            || TryMatch(primaryQuestion, originalPrimaryQuestion, DoYouKnowPattern, requireNamedEntity: true, allowSingleTokenNamedEntity: true, out lookup)
            || TryMatch(primaryQuestion, originalPrimaryQuestion, TellMeAboutPattern, requireNamedEntity: true, allowSingleTokenNamedEntity: true, out lookup)
            || TryMatch(primaryQuestion, originalPrimaryQuestion, WhatCompanyIsPattern, requireNamedEntity: true, allowSingleTokenNamedEntity: false, out lookup))
        {
            return true;
        }

        return TryMatch(primaryQuestion, originalPrimaryQuestion, WhatIsPattern, requireNamedEntity: true, allowSingleTokenNamedEntity: false, out lookup);
    }

    private async Task<EntityMemoryLookup?> TryLookupUserEntityAsync(
        AskWorkflowContext context,
        string entity,
        CancellationToken cancellationToken)
    {
        var original = SnapshotContext(context);
        try
        {
            context.QueryClassification = "entity_lookup";
            context.IntentType = "entity_lookup_intent";
            context.RetrievalAllowed = true;
            context.GroundingMode = "source_grounded";
            context.RetrievalDomain = "entity_lookup";
            context.RetrievalDomainTerms = TokenizeEntity(entity);
            context.RetrievalMinScore = 0.35f;
            context.EvidenceRequired = false;

            var retrieval = await _memoryService.RetrieveAsync(context, cancellationToken).ConfigureAwait(false);
            if (!retrieval.IsSuccess || retrieval.Data is null)
            {
                context.MemoryUnavailable = true;
                context.SourceRetrievalUnavailable = true;
                return null;
            }

            var memories = retrieval.Data.MemoryResults
                .Where(memory => IsEntityEvidence(memory.Text, entity)
                                 && IsStructuredEntityProfileEvidence(memory.SourceType, memory.Text, entity)
                                 && memory.RelevanceScore >= 0.55f
                                 && memory.Confidence >= 0.60f)
                .OrderByDescending(memory => memory.RelevanceScore)
                .ThenByDescending(memory => memory.Confidence)
                .Take(2)
                .ToArray();
            var sources = retrieval.Data.SourceResults
                .Where(source => IsEntityEvidence(source.Text, entity)
                                 && IsStructuredEntityProfileEvidence(source.SourceType, source.Text, entity)
                                 && source.RelevanceScore >= 0.35f)
                .OrderByDescending(source => source.RelevanceScore)
                .Take(2)
                .ToArray();

            return memories.Length == 0 && sources.Length == 0
                ? null
                : new EntityMemoryLookup(memories, sources);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.MemoryUnavailable = true;
            context.SourceRetrievalUnavailable = true;
            return null;
        }
        finally
        {
            RestoreContext(context, original);
        }
    }

    private static void ApplySystemTruthAnswer(AskWorkflowContext context, string entity)
    {
        context.Answer = BuildCwiIdentityAnswer();
        context.Route = "entity_system_truth";
        context.EntityFound = true;
        context.QueryClassification = "identity_definition";
        context.QueryClassificationReason = $"Matched entity lookup for governed system truth: {entity}.";
        context.IntentType = "cwi_identity_intent";
        context.TemplateUsed = "system_truth_answer";
        context.ModelName = "canonical-system-truth";
        context.SkipModelInference = true;
        context.ExecutionTarget = "deterministic_runtime";
        context.GroundingMode = "system_truth";
        context.RetrievalAllowed = false;
        context.RetrievalSkippedReason = "entity_system_truth_bypasses_enterprise_packs";
        context.EvidenceRequired = false;
        ClearEvidence(context);
    }

    private static void ApplyPublicEntityAnswer(
        AskWorkflowContext context,
        string entity,
        PublicEntityAnswer publicEntity)
    {
        context.Answer = publicEntity.Answer;
        context.Route = "entity_public_answer";
        context.EntityFound = true;
        context.QueryClassification = "entity_lookup";
        context.QueryClassificationReason = $"Matched known public entity registry entry for {entity}.";
        context.IntentType = "entity_lookup_intent";
        context.TemplateUsed = "direct_answer";
        context.ModelName = publicEntity.Source;
        context.SkipModelInference = true;
        context.ExecutionTarget = "shared_compute";
        context.GroundingMode = "non_grounded";
        context.RetrievalAllowed = false;
        context.RetrievalSkippedReason = "public_entity_answer_bypasses_enterprise_packs";
        context.EvidenceRequired = false;
        ClearEvidence(context);
    }

    private static void ApplyUserEntityAnswer(
        AskWorkflowContext context,
        string entity,
        EntityMemoryLookup lookup)
    {
        var best = lookup.Memories.FirstOrDefault()?.Text
                   ?? lookup.Sources.FirstOrDefault()?.Text
                   ?? string.Empty;
        context.Answer = string.IsNullOrWhiteSpace(best)
            ? $"I found a CWI memory reference for {entity}, but it needs more context before I can summarize it cleanly."
            : $"I found a CWI memory reference for {entity}: {Compact(best, 360)}";
        context.Route = "entity_memory_answer";
        context.EntityFound = true;
        context.QueryClassification = "entity_lookup";
        context.QueryClassificationReason = $"Matched user/work entity evidence for {entity}.";
        context.IntentType = "entity_lookup_intent";
        context.TemplateUsed = "direct_answer";
        context.ModelName = "governed-memory-entity-lookup";
        context.SkipModelInference = true;
        context.ExecutionTarget = "shared_compute";
        context.GroundingMode = "source_grounded";
        context.RetrievalAllowed = true;
        context.RetrievalSkippedReason = null;
        context.EvidenceRequired = false;
        context.MemoryResults = lookup.Memories;
        context.SourceResults = lookup.Sources;
        context.Retrieved.Clear();
        foreach (var source in lookup.Sources)
        {
            context.Retrieved.Add(new RetrievalResult(
                source.SourceId,
                source.ChunkId,
                source.Text,
                source.RelevanceScore));
        }
        context.GraphEvidence.Clear();
    }

    private static void ApplyUnknownEntityClarification(AskWorkflowContext context, string entity)
    {
        context.Answer = NormalizeMultiline($"""
            I do not currently have enough information to identify who you are referring to.

            Can you clarify whether {entity} is:
            1. A personal contact
            2. A public figure
            3. Someone related to your work

            Once I have that context, I can answer precisely.
            """);
        context.Route = "clarification_handler";
        context.EntityFound = false;
        context.QueryClassification = "entity_lookup";
        context.QueryClassificationReason = $"No governed evidence found for entity lookup: {entity}.";
        context.IntentType = "entity_lookup_intent";
        context.TemplateUsed = "clarification";
        context.ModelName = "clarification-handler";
        context.SkipModelInference = true;
        context.ExecutionTarget = "deterministic_runtime";
        context.GroundingMode = "non_grounded";
        context.RetrievalAllowed = false;
        context.RetrievalSkippedReason = "unknown_entity_requires_clarification";
        context.EvidenceRequired = false;
        ClearEvidence(context);
    }

    private static bool TryMatch(
        string question,
        string originalQuestion,
        Regex pattern,
        bool requireNamedEntity,
        bool allowSingleTokenNamedEntity,
        out EntityLookup lookup)
    {
        lookup = default;
        var match = pattern.Match(question);
        if (!match.Success)
        {
            return false;
        }

        var entity = CleanEntity(match.Groups["entity"].Value);
        if (string.IsNullOrWhiteSpace(entity))
        {
            return false;
        }

        var originalEntity = TryExtractOriginalEntity(originalQuestion, pattern, out var extractedOriginalEntity)
            ? extractedOriginalEntity
            : entity;
        if (requireNamedEntity && !LooksLikeNamedEntity(entity, originalEntity, allowSingleTokenNamedEntity))
        {
            return false;
        }

        lookup = new EntityLookup(entity, "entity_lookup_intent");
        return true;
    }

    private static bool TryExtractOriginalEntity(string originalQuestion, Regex pattern, out string entity)
    {
        entity = string.Empty;
        if (string.IsNullOrWhiteSpace(originalQuestion))
        {
            return false;
        }

        var match = pattern.Match(originalQuestion);
        if (!match.Success)
        {
            return false;
        }

        entity = match.Groups["entity"].Value;
        return !string.IsNullOrWhiteSpace(entity);
    }

    private static bool LooksLikeNamedEntity(
        string entity,
        string originalEntity,
        bool allowSingleTokenNamedEntity)
    {
        if (IsCwiEntity(entity) || KnownPublicEntities.ContainsKey(NormalizeEntityKey(entity)))
        {
            return true;
        }

        if (LooksLikeBroadTopicEntity(entity))
        {
            return false;
        }

        if (LooksLikeGeneralKnowledgePhrase(entity))
        {
            return false;
        }

        if (LooksLikePublicFactEntity(entity))
        {
            return false;
        }

        var tokens = TokenizeEntity(entity);
        if (tokens.Count == 0 || tokens.Count > 5)
        {
            return false;
        }

        if (tokens.Count == 1)
        {
            var token = tokens[0];
            return allowSingleTokenNamedEntity
                   && HasSingleTokenCaseSignal(originalEntity)
                   && token.Length >= 2
                   && token.Length <= 8
                   && token.All(char.IsLetterOrDigit)
                   && !GenericSingleWordEntities.Contains(token);
        }

        return tokens.All(token => !EntityNoiseWords.Contains(token))
               && tokens.Count(token => token.Length >= 2) >= 2;
    }

    private static bool LooksLikeBroadTopicPrompt(string normalizedQuestion)
    {
        if (normalizedQuestion.Contains("current topics", StringComparison.Ordinal)
            || normalizedQuestion.Contains("current events", StringComparison.Ordinal)
            || normalizedQuestion.Contains("latest news", StringComparison.Ordinal)
            || normalizedQuestion.Contains("todays news", StringComparison.Ordinal)
            || normalizedQuestion.Contains("today's news", StringComparison.Ordinal))
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

    private static bool LooksLikePublicFactPrompt(string normalizedQuestion)
    {
        return WhatPublicFactPattern.IsMatch(normalizedQuestion)
               || HowManyPeoplePattern.IsMatch(normalizedQuestion)
               || PublicOfficeFactPattern.IsMatch(normalizedQuestion);
    }

    private static bool LooksLikeAdviceOrMethodPrompt(string normalizedQuestion)
    {
        return AdviceOrMethodPromptPattern.IsMatch(normalizedQuestion);
    }

    private static bool LooksLikeUserMemoryRecallPrompt(string normalizedQuestion)
    {
        return UserMemoryRecallPromptPolicy.IsMatch(normalizedQuestion);
    }

    private static bool LooksLikeProactiveCommandPrompt(string normalizedQuestion)
    {
        return ProactiveCommandPatterns.Any(pattern => normalizedQuestion.Contains(pattern, StringComparison.Ordinal));
    }

    private static bool LooksLikeBroadTopicEntity(string entity)
    {
        var tokens = CleanEntity(entity)
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        return tokens.Any(token => BroadTopicWords.Contains(token))
               && tokens.Any(token => token is "topic" or "topics" or "event" or "events" or "news" or "headline" or "headlines" or "trend" or "trends");
    }

    private static bool LooksLikeGeneralKnowledgePhrase(string entity)
    {
        var normalized = NormalizeEntityKey(entity);
        return GeneralKnowledgeEntityPhrases.Any(phrase =>
            normalized.StartsWith($"{phrase} ", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($" {phrase} ", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikePublicFactEntity(string entity)
    {
        var tokens = NormalizeEntityKey(entity)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        return tokens.Any(token => PublicFactAttributeWords.Contains(token))
               && tokens.Any(token => PublicFactPrepositions.Contains(token));
    }

    private static bool HasSingleTokenCaseSignal(string originalEntity)
    {
        var token = originalEntity
            .Replace("?", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("!", string.Empty, StringComparison.Ordinal)
            .Trim()
            .Trim('"', '\'');
        if (token.Length == 0 || token.Contains(' '))
        {
            return false;
        }

        return token.Any(char.IsUpper);
    }

    private static bool TryResolveKnownPublicEntity(string entity, out PublicEntityAnswer answer)
    {
        var key = NormalizeEntityKey(entity);
        if (KnownPublicEntities.TryGetValue(key, out var resolved))
        {
            answer = resolved;
            return true;
        }

        var compactKey = key
            .Replace(" to ", " ", StringComparison.Ordinal)
            .Replace(" at ", " ", StringComparison.Ordinal)
            .Replace(" from ", " ", StringComparison.Ordinal)
            .Replace(" open ai", " openai", StringComparison.Ordinal)
            .Replace("open ai", "openai", StringComparison.Ordinal);
        if (KnownPublicEntities.TryGetValue(compactKey, out resolved))
        {
            answer = resolved;
            return true;
        }

        var tokens = compactKey.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length >= 2
            && tokens[0] == "sam"
            && tokens.Any(token => token is "altman" or "altamn")
            && tokens.Any(token => token is "openai" or "open"))
        {
            answer = KnownPublicEntities["sam altman"];
            return true;
        }

        answer = null!;
        return false;
    }

    private static bool IsCwiEntity(string entity)
    {
        var normalized = NormalizeEntityKey(entity);
        return normalized is "cwi" or "christian wheelwright intelligence";
    }

    private static bool IsHomeNodeEntity(string entity)
    {
        var normalized = NormalizeEntityKey(entity);
        return normalized is "home node" or "cwi home node";
    }

    private static bool IsEntityEvidence(string text, string entity)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var tokens = TokenizeEntity(entity);
        if (tokens.Count == 0)
        {
            return false;
        }

        var requiredMatches = tokens.Count >= 2 ? 2 : 1;
        return tokens.Count(token => text.Contains(token, StringComparison.OrdinalIgnoreCase)) >= requiredMatches;
    }

    private static bool IsStructuredEntityProfileEvidence(string sourceType, string text, string entity)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalizedSourceType = sourceType.Trim().ToLowerInvariant();
        if (!normalizedSourceType.Contains("entity", StringComparison.Ordinal)
            && !normalizedSourceType.Contains("profile", StringComparison.Ordinal))
        {
            return false;
        }

        var normalizedText = text.ToLowerInvariant();
        var hasType = Regex.IsMatch(normalizedText, @"(?:^|\n|\{|,)\s*""?type""?\s*[:=]\s*""?(?:person|org|organization|entity)\b", RegexOptions.CultureInvariant);
        var hasName = Regex.IsMatch(normalizedText, @"(?:^|\n|\{|,)\s*""?(?:name|person|org|organization|entity)""?\s*[:=]", RegexOptions.CultureInvariant)
                      && IsEntityEvidence(text, entity);
        var hasRoleOrAffiliation = Regex.IsMatch(normalizedText, @"(?:^|\n|\{|,)\s*""?(?:role|title|company|organization|org|affiliation)""?\s*[:=]", RegexOptions.CultureInvariant);
        var hasSource = Regex.IsMatch(normalizedText, @"(?:^|\n|\{|,)\s*""?source""?\s*[:=]", RegexOptions.CultureInvariant);

        return hasType && hasName && hasRoleOrAffiliation && hasSource;
    }

    private static IReadOnlyList<string> TokenizeEntity(string entity)
    {
        return CleanEntity(entity)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !EntityNoiseWords.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CleanEntity(string value)
    {
        var cleaned = value
            .Replace("?", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("!", string.Empty, StringComparison.Ordinal)
            .Trim()
            .Trim('"', '\'');
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(cleaned.ToLowerInvariant());
    }

    private static string NormalizeEntityKey(string value)
    {
        return string.Join(' ', CleanEntity(value)
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !EntityNoiseWords.Contains(token)));
    }

    private static string Compact(string value, int maxChars)
    {
        var normalized = NormalizeMultiline(value);
        return normalized.Length <= maxChars
            ? normalized
            : $"{normalized[..maxChars].TrimEnd()}...";
    }

    private static string NormalizeMultiline(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private static string BuildCwiIdentityAnswer()
    {
        return "CWI stands for Christian Wheelwright Intelligence. It is a private, governed, distributed AI system designed to deliver trusted, traceable, user-owned intelligence across owned infrastructure, local nodes, governed packs, memory, retrieval, and provenance.";
    }

    private static void ClearEvidence(AskWorkflowContext context)
    {
        context.Retrieved.Clear();
        context.GraphEvidence.Clear();
        context.MemoryResults = Array.Empty<GovernedMemoryResult>();
        context.SourceResults = Array.Empty<GovernedSourceResult>();
    }

    private static EntityLookupContextSnapshot SnapshotContext(AskWorkflowContext context)
    {
        return new EntityLookupContextSnapshot(
            context.QueryClassification,
            context.QueryClassificationReason,
            context.IntentType,
            context.RetrievalAllowed,
            context.GroundingMode,
            context.RetrievalDomain,
            context.RetrievalDomainTerms,
            context.RetrievalMinScore,
            context.EvidenceRequired,
            context.RetrievalSkippedReason);
    }

    private static void RestoreContext(AskWorkflowContext context, EntityLookupContextSnapshot snapshot)
    {
        context.QueryClassification = snapshot.QueryClassification;
        context.QueryClassificationReason = snapshot.QueryClassificationReason;
        context.IntentType = snapshot.IntentType;
        context.RetrievalAllowed = snapshot.RetrievalAllowed;
        context.GroundingMode = snapshot.GroundingMode;
        context.RetrievalDomain = snapshot.RetrievalDomain;
        context.RetrievalDomainTerms = snapshot.RetrievalDomainTerms;
        context.RetrievalMinScore = snapshot.RetrievalMinScore;
        context.EvidenceRequired = snapshot.EvidenceRequired;
        context.RetrievalSkippedReason = snapshot.RetrievalSkippedReason;
    }

    public readonly record struct EntityLookup(string Entity, string Intent);

    private sealed record PublicEntityAnswer(string Answer, string Source);

    private sealed record EntityMemoryLookup(
        IReadOnlyList<GovernedMemoryResult> Memories,
        IReadOnlyList<GovernedSourceResult> Sources);

    private sealed record EntityLookupContextSnapshot(
        string? QueryClassification,
        string? QueryClassificationReason,
        string? IntentType,
        bool RetrievalAllowed,
        string GroundingMode,
        string? RetrievalDomain,
        IReadOnlyList<string> RetrievalDomainTerms,
        float RetrievalMinScore,
        bool EvidenceRequired,
        string? RetrievalSkippedReason);
}
#nullable disable

