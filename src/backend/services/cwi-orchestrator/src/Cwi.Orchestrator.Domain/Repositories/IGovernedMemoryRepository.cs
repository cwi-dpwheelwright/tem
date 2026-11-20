#nullable enable
using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IGovernedMemoryRepository
{
    Task UpsertMemoryAsync(GovernedMemoryRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<GovernedMemoryRecord>> ListMemoryCandidatesAsync(
        string tenantId,
        string userId,
        string? nodeId,
        int limit,
        CancellationToken cancellationToken);

    Task UpsertConversationSummaryAsync(ConversationSummaryRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationSummaryRecord>> ListConversationSummariesAsync(
        string tenantId,
        string userId,
        string conversationId,
        int limit,
        CancellationToken cancellationToken);
}
#nullable disable
