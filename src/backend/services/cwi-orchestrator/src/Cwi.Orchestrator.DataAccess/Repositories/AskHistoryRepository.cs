using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class AskHistoryRepository : IAskHistoryRepository
{
    private readonly OrchestratorDbContext _db;

    public AskHistoryRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(AskHistoryRecord record, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(record.RequestId))
        {
            var existing = await _db.AskHistoryRecords
                .FirstOrDefaultAsync(x =>
                    x.TenantId == record.TenantId &&
                    x.UserId == record.UserId &&
                    x.RequestId == record.RequestId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                existing.ConversationId = record.ConversationId;
                existing.Question = record.Question;
                existing.Answer = record.Answer;
                existing.TraceId = record.TraceId;
                existing.ProvenanceId = record.ProvenanceId;
                existing.PackName = record.PackName;
                existing.PackHash = record.PackHash;
                existing.ContentSha256 = record.ContentSha256;
                existing.SummaryStatus = record.SummaryStatus;
                existing.TraceJson = record.TraceJson;
                existing.AttachmentsJson = record.AttachmentsJson;
                if (string.IsNullOrWhiteSpace(existing.TurnId))
                {
                    existing.TurnId = record.TurnId;
                }

                await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        _db.AskHistoryRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AskHistoryRecord>> ListAsync(
        string tenantId,
        string userId,
        int limit,
        DateTimeOffset? createdAfter,
        CancellationToken cancellationToken)
    {
        IQueryable<AskHistoryRecord> query = _db.AskHistoryRecords
            .Where(x => x.TenantId == tenantId && x.UserId == userId);

        if (createdAfter is not null)
        {
            query = query.Where(x => x.CreatedUtc >= createdAfter.Value);
        }

        query = query.OrderByDescending(x => x.CreatedUtc);

        if (limit >= 0)
        {
            query = query.Take(limit);
        }

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<int> DeleteOlderThanAsync(
        string tenantId,
        string userId,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        return _db.AskHistoryRecords
            .Where(x => x.TenantId == tenantId && x.UserId == userId && x.CreatedUtc < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public Task<int> DeleteAllAsync(string tenantId, string userId, CancellationToken cancellationToken)
    {
        return _db.AskHistoryRecords
            .Where(x => x.TenantId == tenantId && x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public Task<int> TrimToLatestAsync(
        string tenantId,
        string userId,
        int maxEntries,
        CancellationToken cancellationToken)
    {
        if (maxEntries <= 0)
        {
            return DeleteAllAsync(tenantId, userId, cancellationToken);
        }

        return _db.Database.ExecuteSqlInterpolatedAsync($@"
DELETE FROM ask_history
WHERE id IN (
  SELECT id
  FROM ask_history
  WHERE tenant_id = {tenantId} AND user_id = {userId}
  ORDER BY created_utc DESC, id DESC
  OFFSET {maxEntries}
);
", cancellationToken);
    }
}
