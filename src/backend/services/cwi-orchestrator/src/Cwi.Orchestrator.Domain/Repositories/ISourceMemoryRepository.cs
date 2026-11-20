#nullable enable
using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface ISourceMemoryRepository
{
    Task UpsertSourceAsync(
        SourceMemoryRecord source,
        IReadOnlyList<SourceChunkRecord> chunks,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SourceChunkRecord>> ListChunkCandidatesAsync(
        string tenantId,
        string userId,
        string? nodeId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SourceMemoryRecord>> ListSourcesAsync(
        string tenantId,
        string? userId,
        string? sourceType,
        int limit,
        CancellationToken cancellationToken);

    Task<SourceMemoryRecord?> GetSourceAsync(
        string tenantId,
        string sourceId,
        string? userId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SourceChunkRecord>> ListChunksForSourceAsync(
        string tenantId,
        string sourceId,
        string? userId,
        int limit,
        CancellationToken cancellationToken);
}
#nullable disable
