#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cwi.Common;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Documents;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Orchestrator.Application.Retrieval;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Memory;

public sealed class GovernedMemoryService
{
    private const int HistoryScanLimit = 25;
    private const int DurableMemoryScanLimit = 100;
    private const int ConversationSummaryScanLimit = 3;
    private const int SourceChunkScanLimit = 200;
    private const int MaxMemoryResults = 3;
    private const int MaxSourceResults = 5;
    private static readonly Regex TokenRegex = new("[A-Za-z0-9][A-Za-z0-9_-]{2,}", RegexOptions.Compiled);
    private static readonly Regex ControlledFactEntityRegex = new(
        @"\bcwi[-\s]?rl[-\s]?\d{3,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PrivateCanaryTokenRegex = new(
        @"\bPRIVATE-CANARY-[A-Z0-9_-]+\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "after", "again", "also", "and", "are", "but", "can", "for", "from", "have", "how",
        "into", "just", "like", "need", "only", "our", "should", "that", "the", "this", "what", "when",
        "where", "which", "with", "you", "your"
    };

    private readonly IAskHistoryRepository _historyRepository;
    private readonly IGovernedMemoryRepository _memoryRepository;
    private readonly ISourceMemoryRepository _sourceMemoryRepository;
    private readonly DocumentStore _documentStore;
    private readonly JourneyEmitter _journeyEmitter;
    private readonly ActivatedMemoryArtifactProvider? _activatedMemoryProvider;
    private readonly MemoryFrameworkService _memoryFrameworkService;

    public GovernedMemoryService(
        IAskHistoryRepository historyRepository,
        IGovernedMemoryRepository memoryRepository,
        ISourceMemoryRepository sourceMemoryRepository,
        DocumentStore documentStore,
        JourneyEmitter journeyEmitter,
        ActivatedMemoryArtifactProvider? activatedMemoryProvider = null,
        MemoryFrameworkService? memoryFrameworkService = null)
    {
        _historyRepository = historyRepository;
        _memoryRepository = memoryRepository;
        _sourceMemoryRepository = sourceMemoryRepository;
        _documentStore = documentStore;
        _journeyEmitter = journeyEmitter;
        _activatedMemoryProvider = activatedMemoryProvider;
        _memoryFrameworkService = memoryFrameworkService ?? new MemoryFrameworkService();
    }

    public async Task<ServiceResponse<GovernedMemoryRetrievalResult>> RetrieveAsync(
        AskWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var memoryStarted = DateTimeOffset.UtcNow;
        await _journeyEmitter.EmitAsync(
            "memory_capsule_retrieval",
            "started",
            fromComponent: "cwi-orchestrator",
            toComponent: "cwi-memory-capsule",
            metadata: new Dictionary<string, object?>
            {
                ["tenant_id"] = context.TenantId,
                ["intent_type"] = context.IntentType,
                ["query_classification"] = context.QueryClassification
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var memoryResults = await RetrieveMemoryAsync(context, cancellationToken).ConfigureAwait(false);
        await _journeyEmitter.EmitAsync(
            "memory_capsule_retrieval",
            "completed",
            direction: "response",
            fromComponent: "cwi-memory-capsule",
            toComponent: "cwi-orchestrator",
            durationMs: (long)(DateTimeOffset.UtcNow - memoryStarted).TotalMilliseconds,
            metadata: new Dictionary<string, object?>
            {
                ["result_count"] = memoryResults.Count,
                ["policy_pack"] = "cwi-memory-policy",
                ["retrieval_mode"] = "selective_relevant_memory"
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var sourceStarted = DateTimeOffset.UtcNow;
        await _journeyEmitter.EmitAsync(
            "source_retrieval",
            "started",
            fromComponent: "cwi-orchestrator",
            toComponent: "cwi-source-retrieval",
            metadata: new Dictionary<string, object?>
            {
                ["tenant_id"] = context.TenantId,
                ["top_k"] = Math.Max(1, context.TopK)
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var sourceResults = await RetrieveSourcesAsync(context, cancellationToken).ConfigureAwait(false);
        await _journeyEmitter.EmitAsync(
            "source_retrieval",
            "completed",
            direction: "response",
            fromComponent: "cwi-source-retrieval",
            toComponent: "cwi-orchestrator",
            durationMs: (long)(DateTimeOffset.UtcNow - sourceStarted).TotalMilliseconds,
            metadata: new Dictionary<string, object?>
            {
                ["result_count"] = sourceResults.Count,
                ["retrieval_mode"] = "relevant_chunks_only"
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return ServiceResponse<GovernedMemoryRetrievalResult>.Success(new GovernedMemoryRetrievalResult(
            memoryResults,
            sourceResults));
    }

    public async Task<MemoryUpdateResult> UpdateAfterAnswerAsync(
        AskWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var update = BuildPostAnswerUpdate(context);
        foreach (var decision in update.Created.Where(decision => !string.IsNullOrWhiteSpace(decision.MemoryId)))
        {
            var record = BuildMemoryRecord(context, decision);
            await _memoryRepository.UpsertMemoryAsync(record, cancellationToken).ConfigureAwait(false);
        }

        var summary = await BuildConversationSummaryAsync(context, update.ProvenanceId, cancellationToken).ConfigureAwait(false);
        if (summary is not null)
        {
            await _memoryRepository.UpsertConversationSummaryAsync(summary, cancellationToken).ConfigureAwait(false);
        }

        return update;
    }

    public MemoryUpdateResult BuildPostAnswerUpdate(AskWorkflowContext context)
    {
        var provenanceId = context.TraceId;
        var primaryQuestion = AskQuestionScope.ExtractPrimaryQuestion(context.Question);
        if (IsControlledFactUpdate(context))
        {
            var memoryText = SanitizeControlledFactText(context.MemoryUpdateValue ?? primaryQuestion);
            if (string.IsNullOrWhiteSpace(memoryText) || ContainsSecret(memoryText))
            {
                return new MemoryUpdateResult
                {
                    Ignored = new[]
                    {
                        new MemoryUpdateDecision
                        {
                            Reason = "controlled_fact_sensitive_content_not_stored"
                        }
                    },
                    ProvenanceId = provenanceId
                };
            }

            var memoryHash = HashText(context.TenantId, context.UserId, memoryText);
            var writeScope = ResolveMemoryWriteScope(context);
            return new MemoryUpdateResult
            {
                Created = new[]
                {
                    new MemoryUpdateDecision
                    {
                        MemoryId = $"mem_{memoryHash[..16]}",
                        MemoryScope = writeScope.MemoryScope,
                        Reason = "controlled_fact_memory_update_detected"
                    }
                },
                ProvenanceId = provenanceId
            };
        }

        if (ContainsSecret(primaryQuestion) || ContainsSecret(context.Answer))
        {
            return new MemoryUpdateResult
            {
                Ignored = new[]
                {
                    new MemoryUpdateDecision
                    {
                        Reason = "secret_or_sensitive_content_not_stored"
                    }
                },
                ProvenanceId = provenanceId
            };
        }

        if (LooksDurable(primaryQuestion))
        {
            var memoryHash = HashText(context.TenantId, context.UserId, primaryQuestion);
            var writeScope = ResolveMemoryWriteScope(context);
            return new MemoryUpdateResult
            {
                Created = new[]
                {
                    new MemoryUpdateDecision
                    {
                        MemoryId = $"mem_{memoryHash[..16]}",
                        MemoryScope = writeScope.MemoryScope,
                        Reason = "durable_user_or_project_context_detected"
                    }
                },
                ProvenanceId = provenanceId
            };
        }

        return new MemoryUpdateResult
        {
            Ignored = new[]
            {
                new MemoryUpdateDecision
                {
                    Reason = "temporary_or_not_durable"
                }
            },
            ProvenanceId = provenanceId
        };
    }

    private async Task<IReadOnlyList<GovernedMemoryResult>> RetrieveMemoryAsync(
        AskWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var queryTokens = Tokenize(AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion));
        if (queryTokens.Count == 0)
        {
            return Array.Empty<GovernedMemoryResult>();
        }

        var durable = await RetrieveDurableMemoryAsync(context, queryTokens, cancellationToken).ConfigureAwait(false);
        var summaries = await RetrieveConversationSummariesAsync(context, queryTokens, cancellationToken).ConfigureAwait(false);
        var recentTurns = await RetrieveConversationMemoryAsync(context, queryTokens, cancellationToken).ConfigureAwait(false);
        var activated = _activatedMemoryProvider is null
            ? Array.Empty<GovernedMemoryResult>()
            : await _activatedMemoryProvider.RetrieveAsync(context, cancellationToken).ConfigureAwait(false);

        if (activated.Count > 0)
        {
            await _activatedMemoryProvider!
                .WriteRetrievalLedgerAsync(context, activated, cancellationToken)
                .ConfigureAwait(false);
        }

        var candidates = activated
            .Concat(durable)
            .Concat(summaries)
            .Concat(recentTurns)
            .OrderByDescending(result => result.RelevanceScore)
            .ThenByDescending(result => result.Confidence)
            .ToArray();
        var activeContext = _memoryFrameworkService.BuildActiveContext(
            context,
            candidates,
            ResolveMaxMemoryResults(context));
        ApplyMemoryFrameworkContext(context, activeContext);
        return activeContext.ActiveMemories;
    }

    private static void ApplyMemoryFrameworkContext(
        AskWorkflowContext context,
        MemoryFrameworkResult result)
    {
        context.MemoryFrameworkVersion = result.FrameworkVersion;
        context.MemoryFrameworkDecisions = result.Decisions;
        context.MemoryConflictSummaries = result.Conflicts;
        context.MemoryActiveContextCount = result.ActiveMemories.Count;
        context.MemoryExcludedContextCount = result.Decisions.Count(decision =>
            string.Equals(decision.Outcome, "excluded", StringComparison.OrdinalIgnoreCase));

        if (result.Conflicts.Count > 0 &&
            !context.ResponseWarnings.Contains("memory_conflict_detected", StringComparer.OrdinalIgnoreCase))
        {
            context.ResponseWarnings.Add("memory_conflict_detected");
        }

        if (result.Decisions.Any(decision =>
                decision.Reason.StartsWith("temporal_state_", StringComparison.OrdinalIgnoreCase)) &&
            !context.ResponseWarnings.Contains("memory_non_current_excluded", StringComparer.OrdinalIgnoreCase))
        {
            context.ResponseWarnings.Add("memory_non_current_excluded");
        }
    }

    private async Task<IReadOnlyList<GovernedMemoryResult>> RetrieveDurableMemoryAsync(
        AskWorkflowContext context,
        HashSet<string> queryTokens,
        CancellationToken cancellationToken)
    {
        var records = await _memoryRepository
            .ListMemoryCandidatesAsync(context.TenantId, context.UserId, context.HomeNodeId, DurableMemoryScanLimit, cancellationToken)
            .ConfigureAwait(false);

        return records
            .Select(record => BuildMemoryResult(context, record, queryTokens))
            .Where(result => result is not null)
            .Select(result => result!)
            .ToArray();
    }

    private async Task<IReadOnlyList<GovernedMemoryResult>> RetrieveConversationSummariesAsync(
        AskWorkflowContext context,
        HashSet<string> queryTokens,
        CancellationToken cancellationToken)
    {
        var summaries = await _memoryRepository
            .ListConversationSummariesAsync(
                context.TenantId,
                context.UserId,
                ResolveConversationId(context),
                ConversationSummaryScanLimit,
                cancellationToken)
            .ConfigureAwait(false);

        return summaries
            .Select(summary => BuildMemoryResult(context, summary, queryTokens))
            .Where(result => result is not null)
            .Select(result => result!)
            .ToArray();
    }

    private async Task<IReadOnlyList<GovernedMemoryResult>> RetrieveConversationMemoryAsync(
        AskWorkflowContext context,
        HashSet<string> queryTokens,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId) || string.IsNullOrWhiteSpace(context.UserId))
        {
            return Array.Empty<GovernedMemoryResult>();
        }

        var records = await _historyRepository
            .ListAsync(context.TenantId, context.UserId, HistoryScanLimit, DateTimeOffset.UtcNow.AddDays(-90), cancellationToken)
            .ConfigureAwait(false);

        return records
            .Select(record => BuildMemoryResult(context, record, queryTokens))
            .Where(result => result is not null)
            .Select(result => result!)
            .OrderByDescending(result => result.RelevanceScore)
            .ThenByDescending(result => result.Confidence)
            .Take(MaxMemoryResults)
            .ToArray();
    }

    private async Task<IReadOnlyList<GovernedSourceResult>> RetrieveSourcesAsync(
        AskWorkflowContext context,
        CancellationToken cancellationToken)
    {
        var primaryQuestion = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion);
        var queryTokens = Tokenize(primaryQuestion);
        var persistedChunks = await _sourceMemoryRepository
            .ListChunkCandidatesAsync(context.TenantId, context.UserId, context.HomeNodeId, SourceChunkScanLimit, cancellationToken)
            .ConfigureAwait(false);
        var persistedResults = persistedChunks
            .Select(chunk => BuildSourceResult(chunk, queryTokens))
            .Where(result => result is not null)
            .Select(result => result!)
            .ToArray();

        var rawResults = _documentStore.Search(context.TenantId, context.UserId, primaryQuestion, Math.Max(1, Math.Min(MaxSourceResults, context.TopK)));
        var documentResults = rawResults.Select(result => new GovernedSourceResult
            {
                SourceId = result.DocId,
                ChunkId = result.ChunkId,
                SourceType = "uploaded_source",
                Text = result.Content,
                TenantId = context.TenantId,
                UserId = context.UserId,
                RelevanceScore = result.Score,
                ContentSha256 = HashText(result.DocId, result.ChunkId, result.Content),
                ExtractionMethod = "server_side_text_index"
            })
            .ToArray();

        return persistedResults
            .Concat(documentResults)
            .GroupBy(result => $"{result.SourceId}:{result.ChunkId}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(result => result.RelevanceScore).First())
            .OrderByDescending(result => result.RelevanceScore)
            .Take(MaxSourceResults)
            .ToArray();
    }

    private static GovernedMemoryResult? BuildMemoryResult(
        AskWorkflowContext context,
        GovernedMemoryRecord record,
        HashSet<string> queryTokens)
    {
        if (ContainsSecret(record.MemoryText))
        {
            return null;
        }

        var relevance = IsActivatedMemoryRecallContext(context) && IsActivatedUserMemoryRecord(record)
            ? ScoreActivatedUserMemory(context, record)
            : ScoreText(record.MemoryText, queryTokens, recencyUtc: record.UpdatedUtc);
        if (IsIdentityNameRecallContext(context) && ContainsDeclaredNameStatement(record.MemoryText))
        {
            relevance = Math.Max(relevance, 0.88f);
        }
        else if (IsWorkRoleRecallContext(context) && ContainsSelfWorkRoleStatement(record.MemoryText))
        {
            relevance = Math.Max(relevance, 0.86f);
        }
        else if (IsUserMemoryRecallContext(context)
                 && IsUserOwnedDurableContextRecord(record)
                 && DurableContextMatchesRecallScope(context, record))
        {
            relevance = Math.Max(relevance, ResolveDurableContextRecallRelevance(context));
        }
        else if (IsControlledFactRecallContext(context)
                 && IsControlledFactMemoryRecord(record)
                 && ControlledFactMatchesRecallScope(context, record))
        {
            relevance = Math.Max(relevance, 0.94f);
        }

        if (relevance <= 0)
        {
            return null;
        }

        return new GovernedMemoryResult
        {
            MemoryId = record.MemoryId,
            MemoryScope = record.MemoryScope,
            SourceType = record.MemoryType,
            Text = record.MemoryText,
            TenantId = record.TenantId,
            UserId = record.UserId,
            NodeId = record.NodeId,
            RelevanceScore = relevance,
            Confidence = record.Confidence,
            ContentSha256 = record.ContentSha256,
            ProvenanceId = record.ProvenanceId
        };
    }

    private static GovernedMemoryResult? BuildMemoryResult(
        AskWorkflowContext context,
        ConversationSummaryRecord summary,
        HashSet<string> queryTokens)
    {
        if (ContainsSecret(summary.SummaryText))
        {
            return null;
        }

        var relevance = ScoreText(summary.SummaryText, queryTokens, recencyUtc: summary.UpdatedUtc);
        if (relevance <= 0)
        {
            return null;
        }

        return new GovernedMemoryResult
        {
            MemoryId = summary.SummaryId,
            MemoryScope = "conversation",
            SourceType = "conversation_summary",
            Text = summary.SummaryText,
            TenantId = summary.TenantId,
            UserId = summary.UserId,
            RelevanceScore = relevance,
            Confidence = summary.Confidence,
            ContentSha256 = summary.ContentSha256,
            ProvenanceId = summary.ProvenanceId
        };
    }

    private static GovernedMemoryResult? BuildMemoryResult(
        AskWorkflowContext context,
        AskHistoryRecord record,
        HashSet<string> queryTokens)
    {
        var text = BuildMemoryText(record);
        if (ContainsSecret(text))
        {
            return null;
        }

        var tokens = Tokenize(text);
        var overlap = tokens.Count == 0 ? 0 : tokens.Count(queryTokens.Contains);
        var containsDeclaredName = ContainsDeclaredNameStatement(text);
        var containsWorkRole = ContainsSelfWorkRoleStatement(text);
        if (overlap == 0
            && !(IsIdentityNameRecallContext(context) && containsDeclaredName)
            && !(IsWorkRoleRecallContext(context) && containsWorkRole))
        {
            return null;
        }

        var recencyBoost = record.CreatedUtc >= DateTimeOffset.UtcNow.AddDays(-7) ? 0.08f : 0f;
        var relevance = Math.Min(0.99f, 0.45f + (overlap * 0.12f) + recencyBoost);
        var confidence = Math.Min(0.9f, 0.65f + (overlap * 0.05f));
        if (IsIdentityNameRecallContext(context) && containsDeclaredName)
        {
            relevance = Math.Max(relevance, 0.82f);
            confidence = Math.Max(confidence, 0.80f);
        }
        else if (IsWorkRoleRecallContext(context) && containsWorkRole)
        {
            relevance = Math.Max(relevance, 0.82f);
            confidence = Math.Max(confidence, 0.80f);
        }

        return new GovernedMemoryResult
        {
            MemoryId = $"hist_{record.Id:N}",
            MemoryScope = "conversation",
            SourceType = "conversation_turn",
            Text = text,
            TenantId = record.TenantId,
            UserId = record.UserId,
            RelevanceScore = relevance,
            Confidence = confidence,
            ContentSha256 = HashText(record.Question, record.Answer),
            ProvenanceId = record.TraceId ?? context.TraceId
        };
    }

    private static GovernedSourceResult? BuildSourceResult(SourceChunkRecord chunk, HashSet<string> queryTokens)
    {
        if (queryTokens.Count == 0 || ContainsSecret(chunk.ChunkText))
        {
            return null;
        }

        var relevance = ScoreText(chunk.ChunkText, queryTokens, recencyUtc: chunk.CreatedUtc);
        if (relevance <= 0)
        {
            return null;
        }

        return new GovernedSourceResult
        {
            SourceId = chunk.SourceId,
            ChunkId = chunk.ChunkId,
            SourceType = "uploaded_source",
            Text = Compact(chunk.ChunkText, 1200),
            TenantId = chunk.TenantId,
            UserId = chunk.UserId,
            NodeId = chunk.NodeId,
            RelevanceScore = relevance,
            ContentSha256 = chunk.ContentSha256,
            ExtractionMethod = "server_side_text_index"
        };
    }

    private static string BuildMemoryText(AskHistoryRecord record)
    {
        var question = Compact(record.Question, 280);
        var answer = Compact(record.Answer, 420);
        return $"Prior turn: user asked \"{question}\"; CWI answered \"{answer}\".";
    }

    private static bool LooksDurable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.ToLowerInvariant();
        return normalized.Contains("remember", StringComparison.Ordinal)
               || normalized.Contains("my preferred", StringComparison.Ordinal)
               || normalized.Contains("my preference", StringComparison.Ordinal)
               || normalized.StartsWith("my name is ", StringComparison.Ordinal)
               || normalized.Contains(" my name is ", StringComparison.Ordinal)
               || normalized.StartsWith("i am named ", StringComparison.Ordinal)
               || normalized.Contains(" i am named ", StringComparison.Ordinal)
               || normalized.StartsWith("i'm named ", StringComparison.Ordinal)
               || normalized.Contains(" i'm named ", StringComparison.Ordinal)
               || normalized.StartsWith("i work as ", StringComparison.Ordinal)
               || normalized.Contains(" i work as ", StringComparison.Ordinal)
               || ContainsSelfWorkRoleStatement(text)
               || normalized.Contains("for future", StringComparison.Ordinal)
               || normalized.Contains("going forward", StringComparison.Ordinal)
               || normalized.Contains("project state", StringComparison.Ordinal);
    }

    private static GovernedMemoryRecord BuildMemoryRecord(
        AskWorkflowContext context,
        MemoryUpdateDecision decision)
    {
        var writeScope = ResolveMemoryWriteScope(context);
        var primaryQuestion = AskQuestionScope.ExtractPrimaryQuestion(context.Question);
        var memoryText = BuildMemoryStorageText(ResolveMemoryStorageText(context, primaryQuestion));
        var now = DateTimeOffset.UtcNow;
        return new GovernedMemoryRecord
        {
            Id = Guid.NewGuid(),
            MemoryId = decision.MemoryId!,
            TenantId = context.TenantId,
            UserId = writeScope.UserId,
            NodeId = writeScope.NodeId,
            MemoryScope = writeScope.MemoryScope,
            MemoryType = ResolveMemoryType(context, primaryQuestion),
            MemoryText = memoryText,
            Confidence = 0.82f,
            SensitivityLevel = "normal",
            RetentionPolicy = "durable",
            AccessScope = writeScope.AccessScope,
            LocalOnly = writeScope.LocalOnly,
            SyncAllowed = !writeScope.LocalOnly,
            ContentSha256 = HashText(memoryText),
            ProvenanceId = context.TraceId,
            CreatedUtc = now,
            UpdatedUtc = now
        };
    }

    private async Task<ConversationSummaryRecord?> BuildConversationSummaryAsync(
        AskWorkflowContext context,
        string? provenanceId,
        CancellationToken cancellationToken)
    {
        var primaryQuestion = AskQuestionScope.ExtractPrimaryQuestion(context.Question);
        if (ContainsSecret(primaryQuestion) || ContainsSecret(context.Answer))
        {
            return null;
        }

        var records = await _historyRepository
            .ListAsync(context.TenantId, context.UserId, limit: 8, createdAfter: DateTimeOffset.UtcNow.AddDays(-30), cancellationToken)
            .ConfigureAwait(false);
        if (records.Count < 6)
        {
            return null;
        }

        var coveredTurnIds = records
            .Select(record => record.Id.ToString("N"))
            .Concat(new[] { context.TraceId ?? context.RequestId ?? Guid.NewGuid().ToString("N") })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var summaryText = BuildConversationSummaryText(records, context);
        var now = DateTimeOffset.UtcNow;
        var conversationId = ResolveConversationId(context);
        return new ConversationSummaryRecord
        {
            Id = Guid.NewGuid(),
            SummaryId = $"sum_{HashText(context.TenantId, context.UserId, conversationId)[..16]}",
            TenantId = context.TenantId,
            UserId = context.UserId,
            ConversationId = conversationId,
            SummaryText = summaryText,
            CoveredTurnIdsJson = JsonSerializer.Serialize(coveredTurnIds),
            Confidence = 0.78f,
            ContentSha256 = HashText(summaryText),
            ProvenanceId = provenanceId,
            CreatedUtc = now,
            UpdatedUtc = now
        };
    }

    private static string BuildConversationSummaryText(
        IReadOnlyList<AskHistoryRecord> records,
        AskWorkflowContext context)
    {
        var turnSummaries = records
            .OrderBy(record => record.CreatedUtc)
            .Select(record => Compact(record.Question, 120))
            .Append(Compact(AskQuestionScope.ExtractPrimaryQuestion(context.Question), 120));
        return $"Recent conversation summary: {string.Join(" | ", turnSummaries)}";
    }

    private static string BuildMemoryStorageText(string question)
    {
        return Compact(question, 800);
    }

    private static string ResolveMemoryStorageText(AskWorkflowContext context, string primaryQuestion)
    {
        if (IsControlledFactUpdate(context))
        {
            return SanitizeControlledFactText(context.MemoryUpdateValue ?? primaryQuestion);
        }

        return primaryQuestion;
    }

    private static string SanitizeControlledFactText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sentences = Regex.Split(text, @"(?<=[\.!\?])\s+")
            .Select(sentence => sentence.Trim())
            .Where(sentence => !string.IsNullOrWhiteSpace(sentence))
            .Where(sentence => !ContainsControlledPrivacyCanary(sentence))
            .Select(sentence => PrivateCanaryTokenRegex.Replace(sentence, "<REDACTED>"))
            .ToArray();
        var sanitized = sentences.Length == 0
            ? PrivateCanaryTokenRegex.Replace(text, "<REDACTED>")
            : string.Join(" ", sentences);

        return Compact(sanitized, 800);
    }

    private static bool ContainsControlledPrivacyCanary(string text)
    {
        return text.Contains("internal privacy canary", StringComparison.OrdinalIgnoreCase)
               || text.Contains("private-canary", StringComparison.OrdinalIgnoreCase)
               || text.Contains("must not be repeated", StringComparison.OrdinalIgnoreCase)
               || text.Contains("must not be sent", StringComparison.OrdinalIgnoreCase)
               || text.Contains("must remain local", StringComparison.OrdinalIgnoreCase)
               || text.Contains("must not enter", StringComparison.OrdinalIgnoreCase)
               || text.Contains("must not appear", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveMemoryType(AskWorkflowContext context, string question)
    {
        if (IsControlledFactUpdate(context))
        {
            return "controlled_fact";
        }

        var normalized = question.ToLowerInvariant();
        if (normalized.StartsWith("my name is ", StringComparison.Ordinal)
            || normalized.Contains(" my name is ", StringComparison.Ordinal)
            || normalized.StartsWith("i am named ", StringComparison.Ordinal)
            || normalized.Contains(" i am named ", StringComparison.Ordinal)
            || normalized.StartsWith("i'm named ", StringComparison.Ordinal)
            || normalized.Contains(" i'm named ", StringComparison.Ordinal))
        {
            return "identity";
        }

        if (ContainsSelfWorkRoleStatement(question))
        {
            return "work_role";
        }

        if (normalized.Contains("preferred", StringComparison.Ordinal)
            || normalized.Contains("preference", StringComparison.Ordinal))
        {
            return "preference";
        }

        if (normalized.Contains("project", StringComparison.Ordinal)
            || normalized.Contains("task state", StringComparison.Ordinal))
        {
            return "project_state";
        }

        return "user_context";
    }

    private static MemoryWriteScope ResolveMemoryWriteScope(AskWorkflowContext context)
    {
        var normalized = AskQuestionScope.ExtractPrimaryQuestion(context.Question).ToLowerInvariant();
        if (normalized.Contains("for everyone", StringComparison.Ordinal)
            || normalized.Contains("organization-wide", StringComparison.Ordinal)
            || normalized.Contains("tenant memory", StringComparison.Ordinal)
            || normalized.Contains("team should remember", StringComparison.Ordinal))
        {
            return new MemoryWriteScope("tenant", UserId: null, NodeId: null, AccessScope: "tenant", LocalOnly: false);
        }

        if (!string.IsNullOrWhiteSpace(context.HomeNodeId)
            && (string.Equals(context.HomeNodeLocalPolicyOutcome, "ALLOW_LOCAL_ONLY_NO_EGRESS", StringComparison.OrdinalIgnoreCase)
                || string.Equals(context.ExecutionTarget, "home_node", StringComparison.OrdinalIgnoreCase)))
        {
            return new MemoryWriteScope("home_node", context.UserId, context.HomeNodeId, AccessScope: "home_node_private", LocalOnly: true);
        }

        return new MemoryWriteScope("user", context.UserId, NodeId: null, AccessScope: "user", LocalOnly: false);
    }

    private static string ResolveConversationId(AskWorkflowContext context)
    {
        return string.IsNullOrWhiteSpace(context.ConversationId) ? "default" : context.ConversationId;
    }

    private static int ResolveMaxMemoryResults(AskWorkflowContext context)
    {
        return string.Equals(context.QueryClassification, "memory_graph_request", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "controlled_fact_lookup", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "memory_recall", StringComparison.OrdinalIgnoreCase)
            || AskUploadedContextLookupPolicy.IsUploadedContextLookup(context)
                ? Math.Max(6, Math.Min(16, Math.Max(1, context.TopK) * 2))
                : MaxMemoryResults;
    }

    private static float ScoreText(string text, HashSet<string> queryTokens, DateTimeOffset? recencyUtc)
    {
        if (queryTokens.Count == 0)
        {
            return 0f;
        }

        var tokens = Tokenize(text);
        var overlap = tokens.Count == 0 ? 0 : tokens.Count(queryTokens.Contains);
        if (overlap == 0)
        {
            return 0f;
        }

        var recencyBoost = recencyUtc >= DateTimeOffset.UtcNow.AddDays(-7) ? 0.08f : 0f;
        return Math.Min(0.99f, 0.45f + (overlap * 0.12f) + recencyBoost);
    }

    private static bool IsActivatedMemoryRecallContext(AskWorkflowContext context)
    {
        return string.Equals(context.QueryClassification, "memory_graph_request", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.IntentType, "memory_recall", StringComparison.OrdinalIgnoreCase)
               || AskUploadedContextLookupPolicy.IsUploadedContextLookup(context)
               || (context.RetrievalDomain?.Contains("memory_graph", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool IsUserMemoryRecallContext(AskWorkflowContext context)
    {
        return string.Equals(context.QueryClassification, "memory_graph_request", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.IntentType, "memory_recall", StringComparison.OrdinalIgnoreCase)
               || (context.RetrievalDomain?.Contains("memory_graph", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool IsControlledFactUpdate(AskWorkflowContext context)
    {
        return string.Equals(context.QueryClassification, "controlled_fact_update", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.MemoryUpdateKind, "controlled_fact", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsControlledFactRecallContext(AskWorkflowContext context)
    {
        return string.Equals(context.QueryClassification, "controlled_fact_lookup", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.RetrievalDomain, "controlled_fact", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsControlledFactMemoryRecord(GovernedMemoryRecord record)
    {
        return string.Equals(record.MemoryType, "controlled_fact", StringComparison.OrdinalIgnoreCase)
               || ControlledFactEntityRegex.IsMatch(record.MemoryText ?? string.Empty);
    }

    private static bool ControlledFactMatchesRecallScope(AskWorkflowContext context, GovernedMemoryRecord record)
    {
        var question = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion);
        var questionEntities = ControlledFactEntityRegex.Matches(question)
            .Select(match => NormalizeControlledFactEntity(match.Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (questionEntities.Count == 0)
        {
            return false;
        }

        var memoryEntities = ControlledFactEntityRegex.Matches(record.MemoryText ?? string.Empty)
            .Select(match => NormalizeControlledFactEntity(match.Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return questionEntities.Overlaps(memoryEntities);
    }

    private static string NormalizeControlledFactEntity(string value)
    {
        return Regex.Replace(value.Trim(), @"\s+", "-", RegexOptions.CultureInvariant)
            .ToUpperInvariant();
    }

    private static bool IsIdentityNameRecallContext(AskWorkflowContext context)
    {
        var question = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion).ToLowerInvariant();
        return question.Contains("my name", StringComparison.Ordinal)
               || (string.Equals(context.RetrievalDomain, "identity_memory_graph", StringComparison.OrdinalIgnoreCase)
                   && context.RetrievalDomainTerms.Any(term =>
                       string.Equals(term, "name", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(term, "identity", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsWorkRoleRecallContext(AskWorkflowContext context)
    {
        var question = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion).ToLowerInvariant();
        return question.Contains("do i do for work", StringComparison.Ordinal)
               || question.Contains("do i do professionally", StringComparison.Ordinal)
               || question.Contains("my job", StringComparison.Ordinal)
               || question.Contains("my profession", StringComparison.Ordinal)
               || question.Contains("my role", StringComparison.Ordinal)
               || question.Contains("as a job", StringComparison.Ordinal)
               || (string.Equals(context.RetrievalDomain, "work_memory_graph", StringComparison.OrdinalIgnoreCase)
                   && context.RetrievalDomainTerms.Any(term =>
                       string.Equals(term, "role", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(term, "job", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(term, "profession", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool ContainsDeclaredNameStatement(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return Regex.IsMatch(
            text,
            @"\b(?:my name is|i am named|i'm named|call me)\s+[A-Za-z'’-]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool ContainsSelfWorkRoleStatement(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return Regex.IsMatch(
            text,
            @"\b(?:i work as|i am|i'm|my job is|my profession is|my role is)\s+(?:a|an|the)?\s*(?:[A-Za-z'’-]+\s+){0,4}(?:engineer|developer|architect|designer|analyst|manager|consultant|administrator|technician|programmer|operator|director|specialist)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsActivatedUserMemoryRecord(GovernedMemoryRecord record)
    {
        return string.Equals(record.MemoryScope, "activated_user_memory", StringComparison.OrdinalIgnoreCase)
               || record.MemoryType.Contains("activated_user_memory", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUserOwnedDurableContextRecord(GovernedMemoryRecord record)
    {
        return string.Equals(record.MemoryScope, "user", StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(record.MemoryText)
               && ContainsAny(record.MemoryType, "identity", "work_role", "user_context", "preference", "project_state");
    }

    private static bool DurableContextMatchesRecallScope(AskWorkflowContext context, GovernedMemoryRecord record)
    {
        var retrievalDomain = context.RetrievalDomain ?? string.Empty;
        var haystack = $"{record.MemoryType} {record.MemoryText}";

        if (string.IsNullOrWhiteSpace(retrievalDomain)
            || retrievalDomain.Contains("user_memory_graph", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (retrievalDomain.Contains("identity", StringComparison.OrdinalIgnoreCase))
        {
            return ContainsAny(haystack, "identity", "name", "my name is", "i am named", "i'm named");
        }

        if (retrievalDomain.Contains("work", StringComparison.OrdinalIgnoreCase))
        {
            return ContainsAny(haystack, "work", "role", "job", "profession", "engineer", "developer", "operations", "operator", "career");
        }

        if (retrievalDomain.Contains("personal", StringComparison.OrdinalIgnoreCase))
        {
            return ContainsAny(haystack, "home", "personal", "family", "health", "wellness", "finance", "pets", "preference");
        }

        if (retrievalDomain.Contains("project", StringComparison.OrdinalIgnoreCase))
        {
            return ContainsAny(haystack, "project", "task", "priority", "open loop", "pending", "working on");
        }

        return false;
    }

    private static float ResolveDurableContextRecallRelevance(AskWorkflowContext context)
    {
        return IsBroadProfileRecallContext(context) ? 0.98f : 0.92f;
    }

    private static bool IsBroadProfileRecallContext(AskWorkflowContext context)
    {
        var question = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion).ToLowerInvariant();
        return string.Equals(context.RetrievalDomain, "user_memory_graph", StringComparison.OrdinalIgnoreCase)
               || question.Contains("what do you know about me", StringComparison.Ordinal)
               || question.Contains("what do you understand about me", StringComparison.Ordinal)
               || question.Contains("what have you learned about me", StringComparison.Ordinal)
               || question.Contains("what have you learnt about me", StringComparison.Ordinal)
               || question.Contains("tell me about myself", StringComparison.Ordinal)
               || question.Contains("describe me", StringComparison.Ordinal);
    }

    private static float ScoreActivatedUserMemory(AskWorkflowContext context, GovernedMemoryRecord record)
    {
        var score = 0.74f;
        var question = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion).ToLowerInvariant();
        var retrievalDomain = context.RetrievalDomain ?? string.Empty;
        var haystack = $"{record.MemoryScope} {record.MemoryType} {record.MemoryText}";

        if (question.Contains("what do you know about me", StringComparison.Ordinal)
            || question.Contains("what do you understand about me", StringComparison.Ordinal)
            || question.Contains("what have you learned about me", StringComparison.Ordinal)
            || question.Contains("what have you learnt about me", StringComparison.Ordinal)
            || string.Equals(retrievalDomain, "user_memory_graph", StringComparison.OrdinalIgnoreCase))
        {
            score += 0.12f;
        }

        if (retrievalDomain.Contains("work", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(haystack, "work", "project", "business", "career", "operations", "technology", "mcp", "cwi", "role", "job", "profession", "engineer", "developer"))
        {
            score += 0.18f;
        }

        if (retrievalDomain.Contains("personal", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(haystack, "home", "personal", "family", "health", "wellness", "finance", "pets"))
        {
            score += 0.18f;
        }

        if (retrievalDomain.Contains("open_loop", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(haystack, "open_loop", "open loop", "task", "todo", "to do", "need to", "pending", "follow up", "next action", "blocked"))
        {
            score += 0.18f;
        }

        if (retrievalDomain.Contains("relationship", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(haystack, "relationship", "person", "people", "collaborator", "demitrius", "chad", "jasmin", "owner", "owns"))
        {
            score += 0.18f;
        }

        if (retrievalDomain.Contains("source", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(haystack, "source", "import", "provenance", "chat_history", "memory_capsule", "activated_user_memory"))
        {
            score += 0.12f;
        }

        if (retrievalDomain.Contains("goal", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(haystack, "goal", "goals", "objective", "aim"))
        {
            score += 0.18f;
        }

        foreach (var term in context.RetrievalDomainTerms)
        {
            if (!string.IsNullOrWhiteSpace(term) && haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.05f;
            }
        }

        var recencyBoost = record.UpdatedUtc >= DateTimeOffset.UtcNow.AddDays(-30) ? 0.03f : 0f;
        return Math.Min(0.99f, score + recencyBoost);
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsSecret(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.ToLowerInvariant();
        string[] markers =
        {
            "api key",
            "password",
            "private key",
            "secret token",
            "access token",
            "bearer ",
            "-----begin"
        };

        return markers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }

    private static HashSet<string> Tokenize(string? text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            return tokens;
        }

        foreach (Match match in TokenRegex.Matches(text.ToLowerInvariant()))
        {
            var token = NormalizeToken(match.Value);
            if (!StopWords.Contains(token))
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }

    private static string NormalizeToken(string token)
    {
        if (token.Length > 4 && token.EndsWith("s", StringComparison.Ordinal))
        {
            return token[..^1];
        }

        return token;
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

    private static string HashText(params string?[] parts)
    {
        var joined = string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public sealed record GovernedMemoryRetrievalResult(
    IReadOnlyList<GovernedMemoryResult> MemoryResults,
    IReadOnlyList<GovernedSourceResult> SourceResults);

internal sealed record MemoryWriteScope(
    string MemoryScope,
    string? UserId,
    string? NodeId,
    string AccessScope,
    bool LocalOnly);
#nullable disable
