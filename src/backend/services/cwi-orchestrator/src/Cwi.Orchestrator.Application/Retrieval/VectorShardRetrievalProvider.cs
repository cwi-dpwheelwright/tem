#nullable enable
using Cwi.Common;
using Cwi.Common.Errors;

namespace Cwi.Orchestrator.Application.Retrieval;

public interface IRetrievalProvider
{
    Task<ServiceResponse<IReadOnlyList<RetrievalResult>>> RetrieveAsync(
        string collection,
        string query,
        int topK,
        RetrievalFilter? filter,
        CancellationToken cancellationToken);
}

public sealed class VectorShardRetrievalProvider : IRetrievalProvider
{
    private readonly VectorShard1Client _shard1;
    private readonly VectorShard2Client _shard2;
    private readonly IEmbeddingProvider _embeddingProvider;

    public VectorShardRetrievalProvider(
        VectorShard1Client shard1,
        VectorShard2Client shard2,
        IEmbeddingProvider embeddingProvider)
    {
        _shard1 = shard1;
        _shard2 = shard2;
        _embeddingProvider = embeddingProvider;
    }

    public async Task<ServiceResponse<IReadOnlyList<RetrievalResult>>> RetrieveAsync(
        string collection,
        string query,
        int topK,
        RetrievalFilter? filter,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return ServiceResponse<IReadOnlyList<RetrievalResult>>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Query is required."),
                statusCode: 400,
                message: "Query required");
        }

        var vector = _embeddingProvider.Embed(query);
        var shard1Task = _shard1.SearchAsync(collection, vector, topK, filter, cancellationToken);
        var shard2Task = _shard2.SearchAsync(collection, vector, topK, filter, cancellationToken);

        await Task.WhenAll(shard1Task, shard2Task).ConfigureAwait(false);

        var shard1 = shard1Task.Result;
        var shard2 = shard2Task.Result;

        if (!shard1.IsSuccess && !shard2.IsSuccess)
        {
            return ServiceResponse<IReadOnlyList<RetrievalResult>>.Success(Array.Empty<RetrievalResult>());
        }

        var results = shard1.Data?.Concat(shard2.Data ?? Array.Empty<RetrievalResult>()) ??
            shard2.Data ??
            Array.Empty<RetrievalResult>();

        var ordered = results
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.DocId, StringComparer.Ordinal)
            .ThenBy(item => item.ChunkId, StringComparer.Ordinal)
            .Take(topK)
            .ToList();

        return ServiceResponse<IReadOnlyList<RetrievalResult>>.Success(ordered);
    }
}
#nullable disable
