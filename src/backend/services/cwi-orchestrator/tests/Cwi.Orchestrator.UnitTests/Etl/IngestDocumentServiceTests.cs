#nullable enable
using Cwi.Orchestrator.Application.Documents;
using Cwi.Orchestrator.Application.Etl;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Trace;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Etl;

public sealed class IngestDocumentServiceTests
{
    [Fact]
    public async Task HandleAsync_persists_uploaded_source_and_chunks()
    {
        var sourceRepository = new InMemorySourceMemoryRepository();
        var service = new IngestDocumentService(
            new IngestDocumentSchemaValidator(),
            new TraceLookupStore(),
            new DocumentStore(),
            sourceRepository);

        var result = await service.HandleAsync(new IngestDocumentRequestDto
        {
            Version = "v1",
            TenantId = "tenant-1",
            UserId = "user-1",
            Document = new IngestDocumentPayloadDto
            {
                DocId = "src-123",
                Title = "Chat export",
                MimeType = "text/plain",
                Content = "Memory capsule source chunk with handoff details.",
                ContentEncoding = "plain",
                Source = "cwi-gpt-web-ui",
                Metadata = new Dictionary<string, object?>
                {
                    ["conversation_id"] = "conversation-1",
                    ["extraction_method"] = "server_side_text_index"
                }
            }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sourceRepository.Sources.Should().ContainSingle(source =>
            source.SourceId == "src-123"
            && source.TenantId == "tenant-1"
            && source.UserId == "user-1"
            && source.ConversationId == "conversation-1");
        sourceRepository.Chunks.Should().ContainSingle(chunk =>
            chunk.SourceId == "src-123"
            && chunk.ChunkText.Contains("handoff details", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HandleAsync_activates_chat_history_import_into_governed_memory()
    {
        var sourceRepository = new InMemorySourceMemoryRepository();
        var memoryRepository = new InMemoryGovernedMemoryRepository();
        var ledgerPath = Path.Combine(Path.GetTempPath(), $"cwi-memory-activation-{Guid.NewGuid():N}.jsonl");
        var activationService = new ChatHistoryMemoryActivationService(
            memoryRepository,
            new ImportedChatGraphProjectionBuilder(),
            new ActivatedMemoryOptions
            {
                ActivationLedgerPath = ledgerPath
            });
        var service = new IngestDocumentService(
            new IngestDocumentSchemaValidator(),
            new TraceLookupStore(),
            new DocumentStore(),
            sourceRepository,
            graphIngestionService: null,
            chatHistoryMemoryActivationService: activationService);

        var result = await service.HandleAsync(new IngestDocumentRequestDto
        {
            Version = "v1",
            TenantId = "tenant-1",
            UserId = "user-1",
            Tags = new List<string> { "chat-history-import", "chat_history" },
            Document = new IngestDocumentPayloadDto
            {
                DocId = "chat-export-1",
                Title = "Imported Chat History",
                MimeType = "text/plain",
                Content = """
                    "oai/apps/hasSeenProjectsUpdateNuxTooltip": "2025-06-14T20:09:39.333338+00:00",
                    User: I have been working on the CWI memory activation bridge.
                    Assistant: The next action is to wire imported chat history into governed memory retrieval.
                    User: Demitrius owns orchestrator deployment and Chad owns dashboard follow-up.
                    User: My goal is to make CWI answer from imported memory with provenance.
                    """,
                ContentEncoding = "plain",
                Source = "cwi-gpt-web-ui",
                Metadata = new Dictionary<string, object?>
                {
                    ["import_kind"] = "chat_history"
                }
            }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        memoryRepository.Memories.Should().Contain(memory =>
            memory.MemoryScope == "activated_user_memory"
            && memory.UserId == "user-1"
            && memory.MemoryType.Contains("project_memory", StringComparison.OrdinalIgnoreCase)
            && memory.MemoryText.Contains("CWI memory activation bridge", StringComparison.OrdinalIgnoreCase)
            && memory.ProvenanceId == result.Data!.TraceId);
        memoryRepository.Memories.Should().Contain(memory =>
            memory.MemoryType.Contains("task_memory", StringComparison.OrdinalIgnoreCase)
            && memory.MemoryText.Contains("next action", StringComparison.OrdinalIgnoreCase));
        memoryRepository.Memories.Should().NotContain(memory =>
            memory.MemoryText.Contains("oai/apps/", StringComparison.OrdinalIgnoreCase)
            || memory.MemoryText.Contains("hasSeenProjectsUpdateNuxTooltip", StringComparison.OrdinalIgnoreCase));
        File.Exists(ledgerPath).Should().BeTrue();
        File.ReadAllText(ledgerPath).Should().Contain("memory.runtime.available");

        var memoryService = new GovernedMemoryService(
            new InMemoryAskHistoryRepository(),
            memoryRepository,
            sourceRepository,
            new DocumentStore(),
            JourneyTestHelpers.CreateEmitter());
        var askContext = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "what do you know about me?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "user_memory_graph",
            RetrievalDomainTerms = new[] { "user" },
            EvidenceRequired = true
        };
        var retrieval = await memoryService.RetrieveAsync(askContext, CancellationToken.None);
        askContext.MemoryResults = AskEvidencePolicy.FilterMemoryResults(askContext, retrieval.Data!.MemoryResults);

        await new AskMemoryProfileAnswerStep().ExecuteAsync(
            askContext,
            () => Task.FromResult(Cwi.Common.ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        askContext.Answer.Should().Contain("CWI memory activation bridge");
        askContext.Answer.Should().Contain("Demitrius");
        askContext.Answer.Should().NotContain(result.Data!.TraceId);
        askContext.MemoryResults.Should().Contain(memory => memory.ProvenanceId == result.Data!.TraceId);

        var otherUserContext = new AskWorkflowContext(
            "tenant-1",
            "user-2",
            "what do you know about me?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "user_memory_graph",
            RetrievalDomainTerms = new[] { "user" },
            EvidenceRequired = true
        };
        var otherUserRetrieval = await memoryService.RetrieveAsync(otherUserContext, CancellationToken.None);
        otherUserContext.MemoryResults = AskEvidencePolicy.FilterMemoryResults(
            otherUserContext,
            otherUserRetrieval.Data!.MemoryResults);

        await new AskMemoryProfileAnswerStep().ExecuteAsync(
            otherUserContext,
            () => Task.FromResult(Cwi.Common.ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        otherUserContext.Answer.Should().Contain("I don't have any stored memory");
        otherUserContext.Answer.Should().NotContain("CWI memory activation bridge");
        otherUserContext.Answer.Should().NotContain("Demitrius");
    }

    [Fact]
    public async Task HandleAsync_activates_user_context_upload_into_governed_memory()
    {
        var sourceRepository = new InMemorySourceMemoryRepository();
        var memoryRepository = new InMemoryGovernedMemoryRepository();
        var ledgerPath = Path.Combine(Path.GetTempPath(), $"cwi-context-activation-{Guid.NewGuid():N}.jsonl");
        var activationService = new ChatHistoryMemoryActivationService(
            memoryRepository,
            new ImportedChatGraphProjectionBuilder(),
            new ActivatedMemoryOptions
            {
                ActivationLedgerPath = ledgerPath
            });
        var service = new IngestDocumentService(
            new IngestDocumentSchemaValidator(),
            new TraceLookupStore(),
            new DocumentStore(),
            sourceRepository,
            graphIngestionService: null,
            chatHistoryMemoryActivationService: activationService);

        var result = await service.HandleAsync(new IngestDocumentRequestDto
        {
            Version = "v1",
            TenantId = "tenant-1",
            UserId = "user-1",
            Tags = new List<string> { "context-upload", "uploaded_context" },
            Document = new IngestDocumentPayloadDto
            {
                DocId = "context-harbor-prism",
                Title = "Harbor Prism Context",
                MimeType = "text/markdown",
                Content = """
                    Project Harbor Prism is the current CWI context-upload validation project.
                    Marisol Vega owns the retrieval validation checklist for Project Harbor Prism.
                    OPEN-HARBOR-314: verify that uploaded context becomes user-scoped governed memory.
                    RETRACT-HARBOR-000 is a false marker and must not be used as active context.
                    """,
                ContentEncoding = "plain",
                Source = "cwi-gpt-web-ui",
                Metadata = new Dictionary<string, object?>
                {
                    ["uploaded_via"] = "cwi-gpt-web-ui",
                    ["import_kind"] = "context_document",
                    ["source_type"] = "uploaded_context"
                }
            }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        memoryRepository.Memories.Should().Contain(memory =>
            memory.MemoryId.StartsWith("mem-upload-", StringComparison.OrdinalIgnoreCase)
            && memory.MemoryScope == "activated_user_memory"
            && memory.UserId == "user-1"
            && memory.MemoryText.Contains("Project Harbor Prism", StringComparison.OrdinalIgnoreCase)
            && memory.ProvenanceId == result.Data!.TraceId);
        memoryRepository.Memories.Should().Contain(memory =>
            memory.MemoryType.Contains("relationship_memory", StringComparison.OrdinalIgnoreCase)
            && memory.MemoryText.Contains("Marisol Vega", StringComparison.OrdinalIgnoreCase));
        memoryRepository.Memories.Should().Contain(memory =>
            memory.MemoryType.Contains("task_memory", StringComparison.OrdinalIgnoreCase)
            && memory.MemoryText.Contains("OPEN-HARBOR-314", StringComparison.OrdinalIgnoreCase));
        memoryRepository.Memories.Should().NotContain(memory =>
            memory.MemoryText.Contains("RETRACT-HARBOR-000", StringComparison.OrdinalIgnoreCase));
        File.ReadAllText(ledgerPath).Should().Contain("uploaded_context");

        var memoryService = new GovernedMemoryService(
            new InMemoryAskHistoryRepository(),
            memoryRepository,
            sourceRepository,
            new DocumentStore(),
            JourneyTestHelpers.CreateEmitter());
        var askContext = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "what projects am I working on?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "work_memory_graph",
            RetrievalDomainTerms = new[] { "work", "project" },
            EvidenceRequired = true
        };
        var retrieval = await memoryService.RetrieveAsync(askContext, CancellationToken.None);
        askContext.MemoryResults = AskEvidencePolicy.FilterMemoryResults(askContext, retrieval.Data!.MemoryResults);

        await new AskMemoryProfileAnswerStep().ExecuteAsync(
            askContext,
            () => Task.FromResult(Cwi.Common.ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        askContext.Answer.Should().Contain("Project Harbor Prism");
        askContext.Answer.Should().NotContain(result.Data!.TraceId);
        askContext.MemoryResults.Should().Contain(memory => memory.ProvenanceId == result.Data!.TraceId);

        var otherUserContext = new AskWorkflowContext(
            "tenant-1",
            "user-2",
            "what projects am I working on?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "work_memory_graph",
            RetrievalDomainTerms = new[] { "work", "project" },
            EvidenceRequired = true
        };
        var otherUserRetrieval = await memoryService.RetrieveAsync(otherUserContext, CancellationToken.None);
        otherUserContext.MemoryResults = AskEvidencePolicy.FilterMemoryResults(
            otherUserContext,
            otherUserRetrieval.Data!.MemoryResults);

        await new AskMemoryProfileAnswerStep().ExecuteAsync(
            otherUserContext,
            () => Task.FromResult(Cwi.Common.ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        otherUserContext.Answer.Should().Contain("I don't have any stored memory");
        otherUserContext.Answer.Should().NotContain("Project Harbor Prism");
        otherUserContext.Answer.Should().NotContain("Marisol Vega");
    }

    [Fact]
    public async Task HandleAsync_keeps_uploaded_platform_specs_out_of_current_work_until_active_work_is_confirmed()
    {
        var sourceRepository = new InMemorySourceMemoryRepository();
        var memoryRepository = new InMemoryGovernedMemoryRepository();
        var activationService = new ChatHistoryMemoryActivationService(
            memoryRepository,
            new ImportedChatGraphProjectionBuilder(),
            new ActivatedMemoryOptions());
        var service = new IngestDocumentService(
            new IngestDocumentSchemaValidator(),
            new TraceLookupStore(),
            new DocumentStore(),
            sourceRepository,
            graphIngestionService: null,
            chatHistoryMemoryActivationService: activationService);

        var result = await service.HandleAsync(new IngestDocumentRequestDto
        {
            Version = "v1",
            TenantId = "tenant-1",
            UserId = "user-1",
            Tags = new List<string> { "context-upload", "uploaded_context" },
            Document = new IngestDocumentPayloadDto
            {
                DocId = "polaris-platform-charter",
                Title = "Polaris Platform Charter",
                MimeType = "text/markdown",
                Content = """
                    # Polaris Platform — Evolved Mission Statement and Build Charter
                    Owner: Astrion9 Technologies
                    Status: Draft v0.2 for build alignment
                    Purpose: Convert the current Polaris mission into a build-ready platform charter that incorporates the four Polaris frameworks.
                    Polaris exists to transform raw, ambiguous, heterogeneous, sensitive, or high-value user-submitted data into decision-grade intelligence.
                    Polaris must preserve source truth, expose uncertainty, and avoid unsupported action.
                    """,
                ContentEncoding = "plain",
                Source = "cwi-gpt-web-ui",
                Metadata = new Dictionary<string, object?>
                {
                    ["uploaded_via"] = "cwi-gpt-web-ui",
                    ["import_kind"] = "context_document",
                    ["source_type"] = "uploaded_context"
                }
            }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        memoryRepository.Memories.Should().Contain(memory =>
            memory.MemoryId.StartsWith("mem-upload-", StringComparison.OrdinalIgnoreCase)
            && memory.MemoryText.Contains("Polaris", StringComparison.OrdinalIgnoreCase));

        var memoryService = new GovernedMemoryService(
            new InMemoryAskHistoryRepository(),
            memoryRepository,
            sourceRepository,
            new DocumentStore(),
            JourneyTestHelpers.CreateEmitter());
        var workContext = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "what projects am I working on?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "work_memory_graph",
            RetrievalDomainTerms = new[] { "work", "project" },
            EvidenceRequired = true
        };

        var workRetrieval = await memoryService.RetrieveAsync(workContext, CancellationToken.None);
        workContext.MemoryResults = AskEvidencePolicy.FilterMemoryResults(workContext, workRetrieval.Data!.MemoryResults);
        workContext.SourceResults = AskEvidencePolicy.FilterSourceResults(workContext, workRetrieval.Data.SourceResults);

        await new AskMemoryProfileAnswerStep().ExecuteAsync(
            workContext,
            () => Task.FromResult(Cwi.Common.ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        workContext.MemoryFrameworkDecisions.Should().Contain(decision =>
            decision.Reason == "uploaded_document_not_confirmed_active_work");
        workContext.Answer.Should().NotContain("Polaris");
        workContext.Answer.Should().NotContain("Astrion9");

        var lookupContext = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "Can you explain what Polaris is?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "general_model_knowledge",
            IntentType = "explanation",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded",
            EvidenceRequired = false
        };
        AskUploadedContextLookupPolicy.TryApply(lookupContext, out _).Should().BeTrue();

        var lookupRetrieval = await memoryService.RetrieveAsync(lookupContext, CancellationToken.None);
        lookupContext.MemoryResults = AskEvidencePolicy.FilterMemoryResults(lookupContext, lookupRetrieval.Data!.MemoryResults);
        lookupContext.SourceResults = AskEvidencePolicy.FilterSourceResults(lookupContext, lookupRetrieval.Data.SourceResults);

        await new AskMemoryProfileAnswerStep().ExecuteAsync(
            lookupContext,
            () => Task.FromResult(Cwi.Common.ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        lookupContext.Answer.Should().Contain("Polaris");
        lookupContext.Answer.Should().Contain("Astrion9");
        lookupContext.Route.Should().Be("uploaded_context_memory_retrieval");
    }

    [Fact]
    public async Task HandleAsync_does_not_activate_unscoped_upload_into_user_memory()
    {
        var sourceRepository = new InMemorySourceMemoryRepository();
        var memoryRepository = new InMemoryGovernedMemoryRepository();
        var activationService = new ChatHistoryMemoryActivationService(
            memoryRepository,
            new ImportedChatGraphProjectionBuilder(),
            new ActivatedMemoryOptions());
        var service = new IngestDocumentService(
            new IngestDocumentSchemaValidator(),
            new TraceLookupStore(),
            new DocumentStore(),
            sourceRepository,
            graphIngestionService: null,
            chatHistoryMemoryActivationService: activationService);

        var result = await service.HandleAsync(new IngestDocumentRequestDto
        {
            Version = "v1",
            TenantId = "tenant-1",
            UserId = null,
            Tags = new List<string> { "context-upload" },
            Document = new IngestDocumentPayloadDto
            {
                DocId = "tenant-context",
                Title = "Tenant Context",
                MimeType = "text/plain",
                Content = "Project Harbor Prism is a user-specific context test.",
                ContentEncoding = "plain",
                Source = "cwi-gpt-web-ui",
                Metadata = new Dictionary<string, object?>
                {
                    ["uploaded_via"] = "cwi-gpt-web-ui",
                    ["source_type"] = "uploaded_context"
                }
            }
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sourceRepository.Sources.Should().ContainSingle(source => source.SourceId == "tenant-context");
        memoryRepository.Memories.Should().BeEmpty();
    }

    private sealed class InMemorySourceMemoryRepository : ISourceMemoryRepository
    {
        public List<SourceMemoryRecord> Sources { get; } = new();
        public List<SourceChunkRecord> Chunks { get; } = new();

        public Task UpsertSourceAsync(
            SourceMemoryRecord source,
            IReadOnlyList<SourceChunkRecord> chunks,
            CancellationToken cancellationToken)
        {
            Sources.RemoveAll(existing => existing.TenantId == source.TenantId && existing.SourceId == source.SourceId);
            Sources.Add(source);
            Chunks.RemoveAll(existing => existing.TenantId == source.TenantId && existing.SourceId == source.SourceId);
            Chunks.AddRange(chunks);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SourceChunkRecord>> ListChunkCandidatesAsync(
            string tenantId,
            string userId,
            string? nodeId,
            int limit,
            CancellationToken cancellationToken)
        {
            var chunks = Chunks
                .Where(chunk => chunk.TenantId == tenantId)
                .Where(chunk => string.Equals(chunk.UserId, userId, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToArray();
            return Task.FromResult<IReadOnlyList<SourceChunkRecord>>(chunks);
        }

        public Task<IReadOnlyList<SourceMemoryRecord>> ListSourcesAsync(
            string tenantId,
            string? userId,
            string? sourceType,
            int limit,
            CancellationToken cancellationToken)
        {
            var sources = Sources
                .Where(source => source.TenantId == tenantId)
                .Where(source => string.IsNullOrWhiteSpace(userId)
                    ? source.UserId is null
                    : string.Equals(source.UserId, userId, StringComparison.OrdinalIgnoreCase))
                .Where(source => string.IsNullOrWhiteSpace(sourceType) || source.SourceType == sourceType)
                .Take(limit)
                .ToArray();
            return Task.FromResult<IReadOnlyList<SourceMemoryRecord>>(sources);
        }

        public Task<SourceMemoryRecord?> GetSourceAsync(
            string tenantId,
            string sourceId,
            string? userId,
            CancellationToken cancellationToken)
        {
            var source = Sources.FirstOrDefault(source =>
                source.TenantId == tenantId
                && source.SourceId == sourceId
                && (string.IsNullOrWhiteSpace(userId)
                    ? source.UserId is null
                    : string.Equals(source.UserId, userId, StringComparison.OrdinalIgnoreCase)));
            return Task.FromResult(source);
        }

        public Task<IReadOnlyList<SourceChunkRecord>> ListChunksForSourceAsync(
            string tenantId,
            string sourceId,
            string? userId,
            int limit,
            CancellationToken cancellationToken)
        {
            var chunks = Chunks
                .Where(chunk => chunk.TenantId == tenantId && chunk.SourceId == sourceId)
                .Where(chunk => string.IsNullOrWhiteSpace(userId)
                    ? chunk.UserId is null
                    : string.Equals(chunk.UserId, userId, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToArray();
            return Task.FromResult<IReadOnlyList<SourceChunkRecord>>(chunks);
        }
    }

    private sealed class InMemoryGovernedMemoryRepository : IGovernedMemoryRepository
    {
        public List<GovernedMemoryRecord> Memories { get; } = new();

        public Task UpsertMemoryAsync(GovernedMemoryRecord record, CancellationToken cancellationToken)
        {
            Memories.RemoveAll(existing => existing.TenantId == record.TenantId && existing.MemoryId == record.MemoryId);
            Memories.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<GovernedMemoryRecord>> ListMemoryCandidatesAsync(
            string tenantId,
            string userId,
            string? nodeId,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<GovernedMemoryRecord>>(
                Memories.Where(memory =>
                        memory.TenantId == tenantId
                        && (string.Equals(memory.UserId, userId, StringComparison.OrdinalIgnoreCase)
                            || (memory.UserId is null
                                && (string.Equals(memory.MemoryScope, "tenant", StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(memory.AccessScope, "tenant", StringComparison.OrdinalIgnoreCase)))))
                    .Take(limit)
                    .ToArray());
        }

        public Task UpsertConversationSummaryAsync(ConversationSummaryRecord record, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ConversationSummaryRecord>> ListConversationSummariesAsync(
            string tenantId,
            string userId,
            string conversationId,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ConversationSummaryRecord>>(Array.Empty<ConversationSummaryRecord>());
        }
    }

    private sealed class InMemoryAskHistoryRepository : Cwi.Orchestrator.Domain.Repositories.IAskHistoryRepository
    {
        public Task AddAsync(Cwi.Orchestrator.Domain.Entities.AskHistoryRecord record, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Cwi.Orchestrator.Domain.Entities.AskHistoryRecord>> ListAsync(
            string tenantId,
            string userId,
            int limit,
            DateTimeOffset? createdAfter,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<Cwi.Orchestrator.Domain.Entities.AskHistoryRecord>>(Array.Empty<Cwi.Orchestrator.Domain.Entities.AskHistoryRecord>());
        }

        public Task<int> DeleteOlderThanAsync(
            string tenantId,
            string userId,
            DateTimeOffset cutoff,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        public Task<int> DeleteAllAsync(string tenantId, string userId, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        public Task<int> TrimToLatestAsync(
            string tenantId,
            string userId,
            int maxEntries,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }
}
#nullable disable
