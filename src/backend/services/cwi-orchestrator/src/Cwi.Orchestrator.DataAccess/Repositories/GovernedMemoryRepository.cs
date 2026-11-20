using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class GovernedMemoryRepository : IGovernedMemoryRepository
{
    private readonly OrchestratorDbContext _db;

    public GovernedMemoryRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public async Task UpsertMemoryAsync(GovernedMemoryRecord record, CancellationToken cancellationToken)
    {
        var existing = await _db.GovernedMemoryRecords
            .FirstOrDefaultAsync(x => x.TenantId == record.TenantId && x.MemoryId == record.MemoryId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _db.GovernedMemoryRecords.Add(record);
        }
        else
        {
            existing.UserId = record.UserId;
            existing.NodeId = record.NodeId;
            existing.MemoryScope = record.MemoryScope;
            existing.MemoryType = record.MemoryType;
            existing.MemoryText = record.MemoryText;
            existing.Confidence = record.Confidence;
            existing.SensitivityLevel = record.SensitivityLevel;
            existing.RetentionPolicy = record.RetentionPolicy;
            existing.AccessScope = record.AccessScope;
            existing.LocalOnly = record.LocalOnly;
            existing.SyncAllowed = record.SyncAllowed;
            existing.ContentSha256 = record.ContentSha256;
            existing.ProvenanceId = record.ProvenanceId;
            existing.UpdatedUtc = record.UpdatedUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GovernedMemoryRecord>> ListMemoryCandidatesAsync(
        string tenantId,
        string userId,
        string? nodeId,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedUserId = userId.Trim();
        var query = _db.GovernedMemoryRecords
            .Where(x => x.TenantId == tenantId)
            .Where(x =>
                x.UserId == normalizedUserId ||
                (x.UserId == null && (x.MemoryScope == "tenant" || x.AccessScope == "tenant")));

        if (string.IsNullOrWhiteSpace(nodeId))
        {
            query = query.Where(x => !x.LocalOnly && x.NodeId == null);
        }
        else
        {
            query = query.Where(x => !x.LocalOnly || x.NodeId == nodeId);
        }

        return await query
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task UpsertConversationSummaryAsync(ConversationSummaryRecord record, CancellationToken cancellationToken)
    {
        var existing = await _db.ConversationSummaryRecords
            .FirstOrDefaultAsync(x =>
                x.TenantId == record.TenantId
                && x.UserId == record.UserId
                && x.SummaryId == record.SummaryId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _db.ConversationSummaryRecords.Add(record);
        }
        else
        {
            existing.ConversationId = record.ConversationId;
            existing.SummaryText = record.SummaryText;
            existing.CoveredTurnIdsJson = record.CoveredTurnIdsJson;
            existing.Confidence = record.Confidence;
            existing.ContentSha256 = record.ContentSha256;
            existing.ProvenanceId = record.ProvenanceId;
            existing.UpdatedUtc = record.UpdatedUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConversationSummaryRecord>> ListConversationSummariesAsync(
        string tenantId,
        string userId,
        string conversationId,
        int limit,
        CancellationToken cancellationToken)
    {
        return await _db.ConversationSummaryRecords
            .Where(x => x.TenantId == tenantId && x.UserId == userId && x.ConversationId == conversationId)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
