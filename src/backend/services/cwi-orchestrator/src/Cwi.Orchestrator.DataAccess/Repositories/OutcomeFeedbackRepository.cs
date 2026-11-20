using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class OutcomeFeedbackRepository : IOutcomeFeedbackRepository
{
    private readonly OrchestratorDbContext _db;

    public OutcomeFeedbackRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(OutcomeFeedbackRecord record, CancellationToken cancellationToken)
    {
        _db.OutcomeFeedbackRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<OutcomeFeedbackRecord?> GetByIdempotencyKeyAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return _db.OutcomeFeedbackRecords
            .Where(x => x.IdempotencyKey == idempotencyKey)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<OutcomeFeedbackRecord?> GetLatestAsync(string tenantId, string traceId, CancellationToken cancellationToken)
    {
        return _db.OutcomeFeedbackRecords
            .Where(x => x.TenantId == tenantId && x.TraceId == traceId)
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<int> GetCountAsync(string tenantId, string traceId, string feedbackType, CancellationToken cancellationToken)
    {
        return _db.OutcomeFeedbackRecords
            .Where(x => x.TenantId == tenantId && x.TraceId == traceId && x.FeedbackType == feedbackType)
            .CountAsync(cancellationToken);
    }
}
