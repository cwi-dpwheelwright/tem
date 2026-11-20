using Cwi.Common;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class AskPromptStepTests
{
    [Fact]
    public async Task ExecuteAsync_preserves_frontend_conversation_context_for_follow_up_prompt()
    {
        var question = """
            Primary question:
            That's interesting. What are some of the major surrounding cities?

            The primary question is authoritative. Use the user-provided context below only when it directly supports the primary question.

            Conversation context from this chat:
            Recent chat history (1 turn):

            Turn 1
            User: What is the capital of Texas?
            Assistant: The capital of Texas is Austin.
            """;
        var context = Context(question);
        var step = new AskPromptStep(new PromptBuilder(), null!);

        var result = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.Prompt.Should().Contain("Current question: that is interesting. what are some of the major surrounding cities");
        context.Prompt.Should().Contain("Conversation context from this chat:");
        context.Prompt.Should().Contain("The capital of Texas is Austin.");
    }

    [Fact]
    public async Task ExecuteAsync_adds_recent_runtime_history_for_context_dependent_follow_up()
    {
        var history = new InMemoryAskHistoryRepository();
        await history.AddAsync(new AskHistoryRecord
        {
            Id = Guid.NewGuid(),
            RequestId = "previous-request",
            TenantId = "tenant-1",
            UserId = "user-1",
            ConversationId = "default",
            Question = "What is the capital of Texas?",
            Answer = "The capital of Texas is Austin.",
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        }, CancellationToken.None);
        var context = Context("That's interesting. What are some of the major surrounding cities?");
        var step = new AskPromptStep(new PromptBuilder(), null!, history);

        var result = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.Prompt.Should().Contain("Current question: About Austin: what are some of the major surrounding cities");
        context.Prompt.Should().Contain("Recent conversation context selected by runtime:");
        context.Prompt.Should().Contain("User: What is the capital of Texas?");
        context.Prompt.Should().Contain("Assistant: The capital of Texas is Austin.");
        context.Prompt.Should().Contain("Do not treat it as durable memory or source evidence.");
    }

    private static AskWorkflowContext Context(string question)
    {
        return new AskWorkflowContext(
            "tenant-1",
            "user-1",
            question,
            "personal",
            null,
            "observe",
            5,
            normalization: LanguageNormalizer.Normalize(AskQuestionScope.ExtractPrimaryQuestion(question)))
        {
            QueryClassification = "general_model_knowledge",
            IntentType = "explanation",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded",
            RetrievalSkippedReason = "general_knowledge_does_not_require_enterprise_retrieval"
        };
    }

    private sealed class InMemoryAskHistoryRepository : IAskHistoryRepository
    {
        private readonly List<AskHistoryRecord> _records = new();

        public Task AddAsync(AskHistoryRecord record, CancellationToken cancellationToken)
        {
            _records.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AskHistoryRecord>> ListAsync(
            string tenantId,
            string userId,
            int limit,
            DateTimeOffset? createdAfter,
            CancellationToken cancellationToken)
        {
            var records = _records
                .Where(record =>
                    string.Equals(record.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(record.UserId, userId, StringComparison.OrdinalIgnoreCase)
                    && (createdAfter is null || record.CreatedUtc >= createdAfter.Value))
                .OrderByDescending(record => record.CreatedUtc)
                .Take(limit)
                .ToArray();
            return Task.FromResult<IReadOnlyList<AskHistoryRecord>>(records);
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
}
