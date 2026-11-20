using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class SourceMemoryRepository : ISourceMemoryRepository
{
    private readonly OrchestratorDbContext _db;

    public SourceMemoryRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public async Task UpsertSourceAsync(
        SourceMemoryRecord source,
        IReadOnlyList<SourceChunkRecord> chunks,
        CancellationToken cancellationToken)
    {
        var existing = await _db.SourceMemoryRecords
            .FirstOrDefaultAsync(x => x.TenantId == source.TenantId && x.SourceId == source.SourceId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _db.SourceMemoryRecords.Add(source);
        }
        else
        {
            existing.UserId = source.UserId;
            existing.ConversationId = source.ConversationId;
            existing.NodeId = source.NodeId;
            existing.Filename = source.Filename;
            existing.SourceType = source.SourceType;
            existing.StorageLocation = source.StorageLocation;
            existing.ExtractionMethod = source.ExtractionMethod;
            existing.LocalOnly = source.LocalOnly;
            existing.SyncAllowed = source.SyncAllowed;
            existing.ContentSha256 = source.ContentSha256;
            existing.ProvenanceId = source.ProvenanceId;
        }

        var chunkIds = chunks.Select(chunk => chunk.ChunkId).ToArray();
        var existingChunks = chunkIds.Length == 0
            ? new List<SourceChunkRecord>()
            : await _db.SourceChunkRecords
                .Where(x => x.TenantId == source.TenantId && x.SourceId == source.SourceId && chunkIds.Contains(x.ChunkId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        foreach (var chunk in chunks)
        {
            var existingChunk = existingChunks.FirstOrDefault(x => x.ChunkId == chunk.ChunkId);
            if (existingChunk is null)
            {
                _db.SourceChunkRecords.Add(chunk);
                continue;
            }

            existingChunk.UserId = chunk.UserId;
            existingChunk.ConversationId = chunk.ConversationId;
            existingChunk.NodeId = chunk.NodeId;
            existingChunk.ChunkText = chunk.ChunkText;
            existingChunk.ChunkIndex = chunk.ChunkIndex;
            existingChunk.HeadingContext = chunk.HeadingContext;
            existingChunk.LocalOnly = chunk.LocalOnly;
            existingChunk.ContentSha256 = chunk.ContentSha256;
            existingChunk.EmbeddingId = chunk.EmbeddingId;
            existingChunk.CreatedUtc = chunk.CreatedUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SourceChunkRecord>> ListChunkCandidatesAsync(
        string tenantId,
        string userId,
        string? nodeId,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedUserId = NormalizeUserId(userId);
        var query = _db.SourceChunkRecords
            .Where(x => x.TenantId == tenantId);

        query = normalizedUserId is null
            ? query.Where(x => x.UserId == null)
            : query.Where(x => x.UserId == normalizedUserId);

        if (string.IsNullOrWhiteSpace(nodeId))
        {
            query = query.Where(x => !x.LocalOnly && x.NodeId == null);
        }
        else
        {
            query = query.Where(x => !x.LocalOnly || x.NodeId == nodeId);
        }

        return await query
            .OrderByDescending(x => x.CreatedUtc)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SourceMemoryRecord>> ListSourcesAsync(
        string tenantId,
        string? userId,
        string? sourceType,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedUserId = NormalizeUserId(userId);
        var query = _db.SourceMemoryRecords
            .Where(x => x.TenantId == tenantId);

        query = normalizedUserId is null
            ? query.Where(x => x.UserId == null)
            : query.Where(x => x.UserId == normalizedUserId);

        if (!string.IsNullOrWhiteSpace(sourceType))
        {
            query = query.Where(x => x.SourceType == sourceType);
        }

        return await query
            .OrderByDescending(x => x.CreatedUtc)
            .Take(Math.Max(1, limit))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<SourceMemoryRecord?> GetSourceAsync(
        string tenantId,
        string sourceId,
        string? userId,
        CancellationToken cancellationToken)
    {
        var normalizedUserId = NormalizeUserId(userId);
        var query = _db.SourceMemoryRecords
            .Where(x => x.TenantId == tenantId && x.SourceId == sourceId);

        query = normalizedUserId is null
            ? query.Where(x => x.UserId == null)
            : query.Where(x => x.UserId == normalizedUserId);

        return query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SourceChunkRecord>> ListChunksForSourceAsync(
        string tenantId,
        string sourceId,
        string? userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedUserId = NormalizeUserId(userId);
        var query = _db.SourceChunkRecords
            .Where(x => x.TenantId == tenantId && x.SourceId == sourceId);

        query = normalizedUserId is null
            ? query.Where(x => x.UserId == null)
            : query.Where(x => x.UserId == normalizedUserId);

        return await query
            .OrderBy(x => x.ChunkIndex)
            .Take(Math.Max(1, limit))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static string? NormalizeUserId(string? userId)
    {
        return string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
    }
}
