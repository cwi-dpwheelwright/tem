using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class ConnectorIngestionRepository : IConnectorIngestionRepository
{
    private readonly OrchestratorDbContext _db;

    public ConnectorIngestionRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public Task<NormalizedConnectorRecordEntity?> GetRecordAsync(
        string tenantId,
        string recordId,
        string userId,
        CancellationToken cancellationToken)
    {
        return _db.NormalizedConnectorRecords.FirstOrDefaultAsync(
            record => record.TenantId == tenantId && record.UserId == userId && record.RecordId == recordId,
            cancellationToken);
    }

    public async Task UpsertRecordAsync(
        NormalizedConnectorRecordEntity record,
        CancellationToken cancellationToken)
    {
        var existing = await _db.NormalizedConnectorRecords.FirstOrDefaultAsync(
            item => item.TenantId == record.TenantId && item.UserId == record.UserId && item.RecordId == record.RecordId,
            cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            _db.NormalizedConnectorRecords.Add(record);
        }
        else
        {
            existing.AccountId = record.AccountId;
            existing.ConnectorType = record.ConnectorType;
            existing.SourceSystem = record.SourceSystem;
            existing.SourceRecordId = record.SourceRecordId;
            existing.SourceThreadId = record.SourceThreadId;
            existing.SourceUrl = record.SourceUrl;
            existing.EventTime = record.EventTime;
            existing.IngestedAt = record.IngestedAt;
            existing.Title = record.Title;
            existing.Body = record.Body;
            existing.RawExcerpt = record.RawExcerpt;
            existing.LifeWorkScope = record.LifeWorkScope;
            existing.SensitivityLabel = record.SensitivityLabel;
            existing.Confidence = record.Confidence;
            existing.SourceSha256 = record.SourceSha256;
            existing.RecordJson = record.RecordJson;
            existing.ProvenanceJson = record.ProvenanceJson;
            existing.SyncStateJson = record.SyncStateJson;
            existing.IsDeleted = record.IsDeleted;
            existing.IsUpdate = record.IsUpdate;
            existing.Stale = record.Stale;
            existing.UpdatedUtc = record.UpdatedUtc;
            existing.LastSeenUtc = record.LastSeenUtc;
            existing.ReplayCount += 1;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<MemoryFactIndexRecord?> GetMemoryFactAsync(
        string tenantId,
        string factKey,
        string userId,
        CancellationToken cancellationToken)
    {
        return _db.MemoryFactIndexRecords.FirstOrDefaultAsync(
            fact => fact.TenantId == tenantId && fact.UserId == userId && fact.FactKey == factKey,
            cancellationToken);
    }

    public async Task UpsertMemoryFactAsync(
        MemoryFactIndexRecord fact,
        CancellationToken cancellationToken)
    {
        var existing = await _db.MemoryFactIndexRecords.FirstOrDefaultAsync(
            item => item.TenantId == fact.TenantId && item.UserId == fact.UserId && item.FactKey == fact.FactKey,
            cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            _db.MemoryFactIndexRecords.Add(fact);
        }
        else
        {
            existing.LifeWorkScope = fact.LifeWorkScope;
            existing.MemoryDomain = fact.MemoryDomain;
            existing.MemoryType = fact.MemoryType;
            existing.NormalizedFactText = fact.NormalizedFactText;
            existing.RecordIdsJson = fact.RecordIdsJson;
            existing.Confidence = Math.Max(existing.Confidence, fact.Confidence);
            existing.UpdatedUtc = fact.UpdatedUtc;
            existing.LastSeenUtc = fact.LastSeenUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddReceiptAsync(
        ConnectorIngestionReceipt receipt,
        CancellationToken cancellationToken)
    {
        _db.ConnectorIngestionReceipts.Add(receipt);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
