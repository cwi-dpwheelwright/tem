#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;

namespace Cwi.Orchestrator.Application.Retrieval;

public interface IVectorShardClient
{
    Task<ServiceResponse<IReadOnlyList<RetrievalResult>>> SearchAsync(
        string collection,
        float[] vector,
        int limit,
        RetrievalFilter? filter,
        CancellationToken cancellationToken);

    Task<ServiceResponse<string>> UpsertAsync(
        string collection,
        VectorShardUpsertPoint point,
        int vectorSize,
        CancellationToken cancellationToken);
}

public sealed record VectorShardUpsertPoint(
    Guid PointId,
    float[] Vector,
    IReadOnlyDictionary<string, object?> Payload);

public class QdrantVectorShardClient : IVectorShardClient
{
    private readonly HttpClient _httpClient;

    public QdrantVectorShardClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ServiceResponse<IReadOnlyList<RetrievalResult>>> SearchAsync(
        string collection,
        float[] vector,
        int limit,
        RetrievalFilter? filter,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(collection))
        {
            return ServiceResponse<IReadOnlyList<RetrievalResult>>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Collection name is required."),
                statusCode: 400,
                message: "Collection name required");
        }

        var request = new QdrantSearchRequest
        {
            Vector = vector,
            Limit = limit,
            WithPayload = true,
            WithVectors = false,
            Filter = BuildFilter(filter)
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                $"collections/{collection}/points/search",
                request,
                timeoutCts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return ServiceResponse<IReadOnlyList<RetrievalResult>>.Fail(
                    new DomainError(ErrorCodes.DependencyUnavailable, "Vector shard query failed."),
                    statusCode: (int)response.StatusCode,
                    message: "Vector shard error");
            }

            var payload = await response.Content
                .ReadFromJsonAsync<QdrantSearchResponse>(cancellationToken: timeoutCts.Token)
                .ConfigureAwait(false);

            var results = payload?.Result?
                .Select(point => MapResult(point))
                .Where(result => result is not null)
                .Select(result => result!)
                .ToList() ?? new List<RetrievalResult>();

            return ServiceResponse<IReadOnlyList<RetrievalResult>>.Success(results);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return ServiceResponse<IReadOnlyList<RetrievalResult>>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Vector shard request failed."),
                statusCode: 503,
                message: "Vector shard error");
        }
    }

    public async Task<ServiceResponse<string>> UpsertAsync(
        string collection,
        VectorShardUpsertPoint point,
        int vectorSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(collection))
        {
            return ServiceResponse<string>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Collection name is required."),
                statusCode: 400,
                message: "Collection name required");
        }

        if (vectorSize <= 0 || point.Vector.Length != vectorSize)
        {
            return ServiceResponse<string>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Vector dimensions do not match the configured collection size."),
                statusCode: 400,
                message: "Vector dimensions invalid");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var escapedCollection = Uri.EscapeDataString(collection.Trim());
            var createRequest = new QdrantCreateCollectionRequest
            {
                Vectors = new QdrantVectorConfig
                {
                    Size = vectorSize,
                    Distance = "Cosine"
                }
            };

            using var createResponse = await _httpClient.PutAsJsonAsync(
                $"collections/{escapedCollection}",
                createRequest,
                timeoutCts.Token).ConfigureAwait(false);

            if (!createResponse.IsSuccessStatusCode && createResponse.StatusCode != HttpStatusCode.Conflict)
            {
                return ServiceResponse<string>.Fail(
                    new DomainError(ErrorCodes.DependencyUnavailable, "Vector collection ensure failed."),
                    statusCode: (int)createResponse.StatusCode,
                    message: "Vector collection error");
            }

            var upsertRequest = new QdrantUpsertRequest
            {
                Points =
                {
                    new QdrantUpsertPoint
                    {
                        Id = point.PointId,
                        Vector = point.Vector,
                        Payload = point.Payload
                    }
                }
            };

            using var upsertResponse = await _httpClient.PutAsJsonAsync(
                $"collections/{escapedCollection}/points?wait=true",
                upsertRequest,
                timeoutCts.Token).ConfigureAwait(false);

            if (!upsertResponse.IsSuccessStatusCode)
            {
                return ServiceResponse<string>.Fail(
                    new DomainError(ErrorCodes.DependencyUnavailable, "Vector shard upsert failed."),
                    statusCode: (int)upsertResponse.StatusCode,
                    message: "Vector shard upsert error");
            }

            return ServiceResponse<string>.Success(point.PointId.ToString("D"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return ServiceResponse<string>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Vector shard upsert request failed."),
                statusCode: 503,
                message: "Vector shard upsert error");
        }
    }

    private static RetrievalResult? MapResult(QdrantPoint point)
    {
        if (point.Payload is null)
        {
            return null;
        }

        var docId = GetPayloadString(point.Payload, "doc_id");
        var chunkId = GetPayloadString(point.Payload, "chunk_id");
        var content = GetPayloadString(point.Payload, "content");

        if (string.IsNullOrWhiteSpace(docId) || string.IsNullOrWhiteSpace(chunkId))
        {
            return null;
        }

        return new RetrievalResult(docId, chunkId, content ?? string.Empty, point.Score);
    }

    private static string? GetPayloadString(Dictionary<string, JsonElement> payload, string key)
    {
        if (!payload.TryGetValue(key, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.GetRawText()
        };
    }

    private static QdrantFilter? BuildFilter(RetrievalFilter? filter)
    {
        if (filter is null)
        {
            return null;
        }

        var must = new List<QdrantCondition>();

        if (!string.IsNullOrWhiteSpace(filter.PackId))
        {
            must.Add(QdrantCondition.Create("pack_id", filter.PackId.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(filter.PackHash))
        {
            must.Add(QdrantCondition.Create("pack_hash", filter.PackHash.Trim()));
        }
        else if (!string.IsNullOrWhiteSpace(filter.PackVersion))
        {
            must.Add(QdrantCondition.Create("pack_version", filter.PackVersion.Trim()));
        }

        return must.Count == 0 ? null : new QdrantFilter { Must = must };
    }

    private sealed class QdrantSearchRequest
    {
        public float[] Vector { get; set; } = Array.Empty<float>();
        public int Limit { get; set; }
        public bool WithPayload { get; set; }
        public bool WithVectors { get; set; }
        public QdrantFilter? Filter { get; set; }
    }

    private sealed class QdrantFilter
    {
        public List<QdrantCondition> Must { get; set; } = new();
    }

    private sealed class QdrantCondition
    {
        public string Key { get; set; } = string.Empty;
        public QdrantMatch Match { get; set; } = new();

        public static QdrantCondition Create(string key, string value)
        {
            return new QdrantCondition
            {
                Key = key,
                Match = new QdrantMatch { Value = value }
            };
        }
    }

    private sealed class QdrantMatch
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class QdrantSearchResponse
    {
        public List<QdrantPoint>? Result { get; set; }
    }

    private sealed class QdrantPoint
    {
        public float Score { get; set; }
        public Dictionary<string, JsonElement>? Payload { get; set; }
    }

    private sealed class QdrantCreateCollectionRequest
    {
        public QdrantVectorConfig Vectors { get; set; } = new();
    }

    private sealed class QdrantVectorConfig
    {
        public int Size { get; set; }
        public string Distance { get; set; } = "Cosine";
    }

    private sealed class QdrantUpsertRequest
    {
        public List<QdrantUpsertPoint> Points { get; } = new();
    }

    private sealed class QdrantUpsertPoint
    {
        public Guid Id { get; set; }
        public float[] Vector { get; set; } = Array.Empty<float>();
        public IReadOnlyDictionary<string, object?> Payload { get; set; } = new Dictionary<string, object?>();
    }
}

public sealed class VectorShard1Client : QdrantVectorShardClient
{
    public VectorShard1Client(HttpClient httpClient) : base(httpClient)
    {
    }
}

public sealed class VectorShard2Client : QdrantVectorShardClient
{
    public VectorShard2Client(HttpClient httpClient) : base(httpClient)
    {
    }
}
#nullable disable
