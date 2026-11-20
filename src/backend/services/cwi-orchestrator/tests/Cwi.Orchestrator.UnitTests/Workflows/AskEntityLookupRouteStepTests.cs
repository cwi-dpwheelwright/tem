#nullable enable
using Cwi.Common;
using Cwi.Orchestrator.Application.Documents;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Trace;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class AskEntityLookupRouteStepTests
{
    [Fact]
    public async Task ExecuteAsync_clarifies_unknown_entity_before_packs()
    {
        var step = CreateStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", "who is shafiq jadallah", "enterprise", null, "observe", 3);
        var nextCalled = false;

        var response = await step.ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeFalse();
        response.Data!.Answer.Should().Contain("Can you clarify");
        response.Data.Trace.Route.Should().Be("clarification_handler");
        response.Data.Trace.EntityFound.Should().BeFalse();
        response.Data.Trace.PackUsed.Should().BeFalse();
        response.Data.Trace.MemoryUsed.Should().BeFalse();
        response.Data.Trace.GraphUsed.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_answers_known_public_entity_without_enterprise_pack()
    {
        var step = CreateStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", "who is Elon Musk", "enterprise", null, "observe", 3);

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Answer.Should().Contain("Elon Musk");
        response.Data.Trace.Route.Should().Be("entity_public_answer");
        response.Data.Trace.EntityFound.Should().BeTrue();
        response.Data.Trace.PackUsed.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_answers_cwi_as_system_truth_without_enterprise_pack()
    {
        var step = CreateStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", "who is CWI", "enterprise", null, "observe", 3);

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Answer.Should().Contain("Christian Wheelwright Intelligence");
        response.Data.Trace.Route.Should().Be("entity_system_truth");
        response.Data.Trace.EntityFound.Should().BeTrue();
        response.Data.Trace.PackUsed.Should().BeFalse();
    }

    [Theory]
    [InlineData("who is Sam Altman")]
    [InlineData("who is sam altamn to open ai")]
    public async Task ExecuteAsync_answers_known_public_person_without_unknown_entity_clarification(string question)
    {
        var step = CreateStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", question, "enterprise", null, "observe", 3);

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Answer.Should().Contain("Sam Altman");
        response.Data.Answer.Should().Contain("OpenAI");
        response.Data.Trace.Route.Should().Be("entity_public_answer");
        response.Data.Trace.EntityFound.Should().BeTrue();
        response.Data.Trace.PackUsed.Should().BeFalse();
        response.Data.Trace.MemoryUsed.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_does_not_treat_plain_general_knowledge_question_as_unknown_entity()
    {
        var step = CreateStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", "what is oceaneering", "enterprise", null, "observe", 3);
        var nextCalled = false;

        var response = await step.ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
        context.EntityFound.Should().BeNull();
        context.Route.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_does_not_steal_controlled_fact_lookups()
    {
        var step = CreateStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", "What is the stable routing color for CWI-RL-041?", "enterprise", null, "observe", 3)
        {
            QueryClassification = "controlled_fact_lookup",
            IntentType = "memory_recall",
            RetrievalDomain = "controlled_fact"
        };
        var nextCalled = false;

        var response = await step.ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
        context.QueryClassification.Should().Be("controlled_fact_lookup");
        context.Route.Should().BeNull();
    }

    [Theory]
    [InlineData("What is the capital of Texas?")]
    [InlineData("What is the capitol of Texas?")]
    [InlineData("What is the capital of France?")]
    [InlineData("What is the population in Texas?")]
    [InlineData("What is the population of Texas?")]
    [InlineData("How many people live in Texas?")]
    [InlineData("What is the area of Texas?")]
    [InlineData("What is the largest city in Texas?")]
    [InlineData("Who is the governor of Texas?")]
    [InlineData("What are the major cities around Austin?")]
    public async Task ExecuteAsync_does_not_treat_public_fact_questions_as_unknown_entities(string question)
    {
        var step = CreateStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", question, "enterprise", null, "observe", 3);
        var nextCalled = false;

        var response = await step.ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
        context.EntityFound.Should().BeNull();
        context.Route.Should().BeNull();
    }

    [Theory]
    [InlineData("What is a good way to track spending?")]
    [InlineData("What is the best way to remove a coffee stain?")]
    [InlineData("What is a quick breakfast I can make in five minutes?")]
    [InlineData("How do I compare two laptop options?")]
    public async Task ExecuteAsync_does_not_treat_advice_or_method_prompts_as_unknown_entities(string question)
    {
        var step = CreateStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", question, "enterprise", null, "observe", 3);
        var nextCalled = false;

        var response = await step.ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
        context.EntityFound.Should().BeNull();
        context.Route.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_does_not_treat_broad_current_topics_as_unknown_entity()
    {
        var step = CreateStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", "What are the world's current topics?", "enterprise", null, "observe", 3);
        var nextCalled = false;

        var response = await step.ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
        context.EntityFound.Should().BeNull();
        context.Route.Should().BeNull();
    }

    [Theory]
    [InlineData("whats teh wether tmrw do i need jaket")]
    [InlineData("wht do i have tomorow")]
    [InlineData("reed my emails from chad")]
    [InlineData("who won teh game last nite")]
    public async Task ExecuteAsync_leaves_tool_and_current_info_prompts_for_classifier(string question)
    {
        var step = CreateStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", question, "enterprise", null, "observe", 3);
        var nextCalled = false;

        var response = await step.ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
        context.EntityFound.Should().BeNull();
        context.Route.Should().BeNull();
    }

    [Theory]
    [InlineData("what's at risk right now")]
    [InlineData("daily briefing")]
    [InlineData("what changed")]
    [InlineData("make a plan")]
    public async Task ExecuteAsync_leaves_proactive_commands_for_response_mode_routing(string question)
    {
        var step = CreateStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", question, "enterprise", null, "observe", 3);
        var nextCalled = false;

        var response = await step.ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
        context.EntityFound.Should().BeNull();
        context.Route.Should().BeNull();
    }

    [Theory]
    [InlineData("what do you know about me?")]
    [InlineData("WHAT DO YOU UNDERSTAND ABOUT ME")]
    [InlineData("what projects am I working on?")]
    [InlineData("who are the key people in my network?")]
    [InlineData("what open loops do I have?")]
    [InlineData("show sources for what you know about me")]
    [InlineData("Can you describe me as a person?")]
    public async Task ExecuteAsync_leaves_user_memory_profile_prompts_for_memory_routing(string question)
    {
        var step = CreateStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", question, "enterprise", null, "observe", 3);
        var nextCalled = false;

        var response = await step.ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
        context.EntityFound.Should().BeNull();
        context.Route.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_normalizes_misspelled_identity_lookup_before_packs()
    {
        var step = CreateStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", "wht is CWI", "enterprise", null, "observe", 3);

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Answer.Should().Contain("Christian Wheelwright Intelligence");
        response.Data.Trace.Route.Should().Be("entity_system_truth");
        response.Data.Trace.NormalizationApplied.Should().BeTrue();
        response.Data.Trace.NormalizedPrompt.Should().Be("what is cwi");
        response.Data.Trace.PackUsed.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_answers_known_user_entity_from_memory()
    {
        var memoryRepository = new InMemoryGovernedMemoryRepository();
        await memoryRepository.UpsertMemoryAsync(new GovernedMemoryRecord
        {
            Id = Guid.NewGuid(),
            MemoryId = "mem-demitrius-runtime",
            TenantId = "tenant-1",
            UserId = "user-1",
            MemoryScope = "user",
            MemoryType = "entity_profile",
            MemoryText = """
                type: person
                name: Demitrius
                role: responsible for runtime routing fixes and demo-readiness work
                source: user-confirmed entity profile
                """,
            Confidence = 0.82f,
            SensitivityLevel = "normal",
            RetentionPolicy = "durable",
            AccessScope = "user",
            ContentSha256 = "sha256",
            ProvenanceId = "prov-demitrius",
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        var step = CreateStep(memoryRepository);
        var context = new AskWorkflowContext("tenant-1", "user-1", "who is Demitrius", "enterprise", null, "observe", 3);

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Answer.Should().Contain("runtime routing fixes");
        response.Data.Trace.Route.Should().Be("entity_memory_answer");
        response.Data.Trace.EntityFound.Should().BeTrue();
        response.Data.Trace.MemoryUsed.Should().BeTrue();
        response.Data.Trace.PackUsed.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_does_not_use_raw_prior_chat_mention_as_entity_evidence()
    {
        var memoryRepository = new InMemoryGovernedMemoryRepository();
        await memoryRepository.UpsertMemoryAsync(new GovernedMemoryRecord
        {
            Id = Guid.NewGuid(),
            MemoryId = "mem-glesby-chat",
            TenantId = "tenant-1",
            UserId = "user-1",
            MemoryScope = "conversation",
            MemoryType = "conversation_turn",
            MemoryText = "User previously mentioned Glesby Marks in a chat about a possible company.",
            Confidence = 0.9f,
            SensitivityLevel = "normal",
            RetentionPolicy = "durable",
            AccessScope = "user",
            ContentSha256 = "sha256-glesby",
            ProvenanceId = "prov-glesby",
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        var step = CreateStep(memoryRepository);
        var context = new AskWorkflowContext("tenant-1", "user-1", "what company is Glesby Marks", "enterprise", null, "observe", 3);

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Answer.Should().Contain("Can you clarify");
        response.Data.Answer.Should().NotContain("I found a CWI memory reference");
        response.Data.Trace.Route.Should().Be("clarification_handler");
        response.Data.Trace.EntityFound.Should().BeFalse();
        response.Data.Trace.MemoryUsed.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_clarifies_random_two_word_name()
    {
        var step = CreateStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", "who is random two-word name", "enterprise", null, "observe", 3);

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Answer.Should().Contain("Can you clarify");
        response.Data.Trace.Route.Should().Be("clarification_handler");
        response.Data.Trace.EntityFound.Should().BeFalse();
    }

    private static AskEntityLookupRouteStep CreateStep(
        InMemoryGovernedMemoryRepository? memoryRepository = null)
    {
        var memory = new GovernedMemoryService(
            new InMemoryAskHistoryRepository(),
            memoryRepository ?? new InMemoryGovernedMemoryRepository(),
            new InMemorySourceMemoryRepository(),
            new DocumentStore(),
            JourneyTestHelpers.CreateEmitter());

        return new AskEntityLookupRouteStep(
            memory,
            JourneyTestHelpers.CreateContext(),
            new TraceLookupStore());
    }

    private sealed class InMemoryAskHistoryRepository : IAskHistoryRepository
    {
        public Task AddAsync(AskHistoryRecord record, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<AskHistoryRecord>> ListAsync(
            string tenantId,
            string userId,
            int limit,
            DateTimeOffset? createdAfter,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<AskHistoryRecord>>(Array.Empty<AskHistoryRecord>());
        }

        public Task<int> DeleteOlderThanAsync(
            string tenantId,
            string userId,
            DateTimeOffset cutoff,
            CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<int> DeleteAllAsync(string tenantId, string userId, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<int> TrimToLatestAsync(
            string tenantId,
            string userId,
            int maxEntries,
            CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class InMemoryGovernedMemoryRepository : IGovernedMemoryRepository
    {
        private readonly List<GovernedMemoryRecord> _memories = new();

        public Task UpsertMemoryAsync(GovernedMemoryRecord record, CancellationToken cancellationToken)
        {
            _memories.RemoveAll(memory => memory.TenantId == record.TenantId && memory.MemoryId == record.MemoryId);
            _memories.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<GovernedMemoryRecord>> ListMemoryCandidatesAsync(
            string tenantId,
            string userId,
            string? nodeId,
            int limit,
            CancellationToken cancellationToken)
        {
            var records = _memories
                .Where(memory => memory.TenantId == tenantId)
                .Where(memory => memory.UserId is null || memory.UserId == userId)
                .Take(limit)
                .ToArray();
            return Task.FromResult<IReadOnlyList<GovernedMemoryRecord>>(records);
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

    private sealed class InMemorySourceMemoryRepository : ISourceMemoryRepository
    {
        public Task UpsertSourceAsync(
            SourceMemoryRecord source,
            IReadOnlyList<SourceChunkRecord> chunks,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<SourceChunkRecord>> ListChunkCandidatesAsync(
            string tenantId,
            string userId,
            string? nodeId,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SourceChunkRecord>>(Array.Empty<SourceChunkRecord>());
        }

        public Task<IReadOnlyList<SourceMemoryRecord>> ListSourcesAsync(
            string tenantId,
            string? userId,
            string? sourceType,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SourceMemoryRecord>>(Array.Empty<SourceMemoryRecord>());
        }

        public Task<SourceMemoryRecord?> GetSourceAsync(
            string tenantId,
            string sourceId,
            string? userId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<SourceMemoryRecord?>(null);
        }

        public Task<IReadOnlyList<SourceChunkRecord>> ListChunksForSourceAsync(
            string tenantId,
            string sourceId,
            string? userId,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SourceChunkRecord>>(Array.Empty<SourceChunkRecord>());
        }
    }
}
#nullable disable
