#nullable enable
using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IOutcomeFeedbackRepository
{
    Task AddAsync(OutcomeFeedbackRecord record, CancellationToken cancellationToken);

    Task<OutcomeFeedbackRecord?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task<OutcomeFeedbackRecord?> GetLatestAsync(string tenantId, string traceId, CancellationToken cancellationToken);

    Task<int> GetCountAsync(string tenantId, string traceId, string feedbackType, CancellationToken cancellationToken);
}
#nullable disable
