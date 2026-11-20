#nullable enable
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Retrieval;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Graph;

public sealed class GraphToVectorRefreshService
{
    private readonly IGraphVectorRefreshJobRepository _repository;
    private readonly IGraphUpdateCandidateRepository? _candidateRepository;
    private readonly VectorShard1Client? _shard1;
    private readonly VectorShard2Client? _shard2;
    private readonly IEmbeddingProvider? _embeddingProvider;
    private readonly ApplicationOptions? _options;

    public GraphToVectorRefreshService(IGraphVectorRefreshJobRepository repository)
    {
        _repository = repository;
    }

    public GraphToVectorRefreshService(
        IGraphVectorRefreshJobRepository repository,
        IGraphUpdateCandidateRepository candidateRepository,
        VectorShard1Client shard1,
        VectorShard2Client shard2,
        IEmbeddingProvider embeddingProvider,
        ApplicationOptions options)
    {
        _repository = repository;
        _candidateRepository = candidateRepository;
        _shard1 = shard1;
        _shard2 = shard2;
        _embeddingProvider = embeddingProvider;
        _options = options;
    }

    public async Task<GraphVectorRefreshCreateResultDto> CreateRefreshJobAsync(
        GraphUpdateCandidateRecord candidate,
        CancellationToken cancellationToken)
    {
        if (await _repository.ExistsForGraphCandidateAsync(candidate.CandidateId, cancellationToken)
                .ConfigureAwait(false))
        {
            return new GraphVectorRefreshCreateResultDto
            {
                Created = false,
                Skipped = true,
                Reason = "refresh_job_already_exists"
            };
        }

        var now = DateTimeOffset.UtcNow;
        var record = new GraphVectorRefreshJobRecord
        {
            Id = Guid.NewGuid(),
            JobId = BuildJobId(candidate),
            TenantId = candidate.TenantId,
            UserIdHash = candidate.UserIdHash,
            NodeId = candidate.NodeId,
            GraphCandidateId = candidate.CandidateId,
            RecursiveEventId = candidate.RecursiveEventId,
            TraceId = candidate.TraceId,
            FactKey = candidate.FactKey,
            RefreshReason = BuildRefreshReason(candidate),
            RefreshStatus = GraphVectorRefreshStatuses.Pending,
            AffectedVectorScope = ResolveAffectedVectorScope(candidate),
            SourceHash = candidate.SourceHash,
            AttemptCount = 0,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        await _repository.AddAsync(record, cancellationToken).ConfigureAwait(false);
        return new GraphVectorRefreshCreateResultDto
        {
            Created = true,
            Skipped = false,
            Job = ToDto(record)
        };
    }

    public async Task<IReadOnlyList<GraphVectorRefreshJobDto>> QueryAsync(
        string? tenantId,
        string? refreshStatus,
        string? graphCandidateId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Min(Math.Max(1, pageSize), 200);
        var records = await _repository.QueryAsync(
            NormalizeOptional(tenantId),
            NormalizeOptional(refreshStatus),
            NormalizeOptional(graphCandidateId),
            (normalizedPage - 1) * normalizedPageSize,
            normalizedPageSize,
            cancellationToken).ConfigureAwait(false);

        return records.Select(ToDto).ToArray();
    }

    public async Task<GraphVectorRefreshProcessResultDto> ProcessPendingAsync(
        string? tenantId,
        int pageSize,
        CancellationToken cancellationToken)
    {
        EnsureProcessorDependencies();

        var normalizedPageSize = Math.Min(Math.Max(1, pageSize), 100);
        var pendingJobs = await _repository.QueryAsync(
            NormalizeOptional(tenantId),
            GraphVectorRefreshStatuses.Pending,
            null,
            0,
            normalizedPageSize,
            cancellationToken).ConfigureAwait(false);

        var results = new List<GraphVectorRefreshJobDto>();
        var completed = 0;
        var failed = 0;

        foreach (var job in pendingJobs)
        {
            var result = await ProcessJobAsync(job.JobId, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                continue;
            }

            results.Add(result);
            if (string.Equals(result.RefreshStatus, GraphVectorRefreshStatuses.Completed, StringComparison.OrdinalIgnoreCase))
            {
                completed += 1;
            }
            else if (string.Equals(result.RefreshStatus, GraphVectorRefreshStatuses.Failed, StringComparison.OrdinalIgnoreCase))
            {
                failed += 1;
            }
        }

        return new GraphVectorRefreshProcessResultDto
        {
            Attempted = pendingJobs.Count,
            Completed = completed,
            Failed = failed,
            Jobs = results
        };
    }

    public async Task<GraphVectorRefreshJobDto?> MarkRunningAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        var record = await _repository.GetByJobIdAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        record.RefreshStatus = GraphVectorRefreshStatuses.Running;
        record.AttemptCount += 1;
        record.UpdatedUtc = DateTimeOffset.UtcNow;
        await _repository.UpdateAsync(record, cancellationToken).ConfigureAwait(false);
        return ToDto(record);
    }

    private async Task<GraphVectorRefreshJobDto?> ProcessJobAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        var running = await MarkRunningAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (running is null)
        {
            return null;
        }

        var candidate = await _candidateRepository!
            .GetByCandidateIdAsync(running.GraphCandidateId, cancellationToken)
            .ConfigureAwait(false);
        if (candidate is null)
        {
            return await MarkFailedAsync(jobId, "graph_candidate_missing", cancellationToken)
                .ConfigureAwait(false);
        }

        var content = BuildVectorContent(candidate);
        var vector = _embeddingProvider!.Embed(content);
        var point = new VectorShardUpsertPoint(
            BuildPointId(running.JobId),
            vector,
            BuildVectorPayload(running, candidate, content));

        var collection = string.IsNullOrWhiteSpace(running.AffectedVectorScope)
            ? "graph_fact_current"
            : running.AffectedVectorScope.Trim();

        var shard1 = await _shard1!.UpsertAsync(
            collection,
            point,
            _options!.VectorEmbeddingDimension,
            cancellationToken).ConfigureAwait(false);
        var shard2 = await _shard2!.UpsertAsync(
            collection,
            point,
            _options.VectorEmbeddingDimension,
            cancellationToken).ConfigureAwait(false);

        if (shard1.IsSuccess || shard2.IsSuccess)
        {
            return await MarkCompletedAsync(jobId, cancellationToken).ConfigureAwait(false);
        }

        var error = string.Join(
            "; ",
            new[]
            {
                $"shard1:{NormalizeError(shard1.Message)}",
                $"shard2:{NormalizeError(shard2.Message)}"
            });
        return await MarkFailedAsync(jobId, error, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GraphVectorRefreshJobDto?> MarkCompletedAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        var record = await _repository.GetByJobIdAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        record.RefreshStatus = GraphVectorRefreshStatuses.Completed;
        record.LastError = null;
        record.CompletedUtc = now;
        record.UpdatedUtc = now;
        await _repository.UpdateAsync(record, cancellationToken).ConfigureAwait(false);
        return ToDto(record);
    }

    public async Task<GraphVectorRefreshJobDto?> MarkFailedAsync(
        string jobId,
        string error,
        CancellationToken cancellationToken)
    {
        var record = await _repository.GetByJobIdAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        record.RefreshStatus = GraphVectorRefreshStatuses.Failed;
        record.LastError = string.IsNullOrWhiteSpace(error) ? "unknown_error" : error.Trim();
        record.UpdatedUtc = DateTimeOffset.UtcNow;
        await _repository.UpdateAsync(record, cancellationToken).ConfigureAwait(false);
        return ToDto(record);
    }

    public static GraphVectorRefreshJobDto ToDto(GraphVectorRefreshJobRecord record)
    {
        return new GraphVectorRefreshJobDto
        {
            JobId = record.JobId,
            TenantId = record.TenantId,
            UserIdHash = record.UserIdHash,
            NodeId = record.NodeId,
            GraphCandidateId = record.GraphCandidateId,
            RecursiveEventId = record.RecursiveEventId,
            TraceId = record.TraceId,
            FactKey = record.FactKey,
            RefreshReason = record.RefreshReason,
            RefreshStatus = record.RefreshStatus,
            AffectedVectorScope = record.AffectedVectorScope,
            SourceHash = record.SourceHash,
            AttemptCount = record.AttemptCount,
            LastError = record.LastError,
            CreatedUtc = record.CreatedUtc,
            UpdatedUtc = record.UpdatedUtc,
            CompletedUtc = record.CompletedUtc
        };
    }

    private static string BuildJobId(GraphUpdateCandidateRecord candidate)
    {
        return $"graph-vector-refresh-{HashText(string.Join(
            "|",
            candidate.TenantId,
            candidate.CandidateId,
            candidate.FactKey,
            candidate.SourceHash))[..24]}";
    }

    private static string BuildRefreshReason(GraphUpdateCandidateRecord candidate)
    {
        return $"graph_candidate_{candidate.CandidateState}_{candidate.TemporalState}";
    }

    private static string BuildVectorContent(GraphUpdateCandidateRecord candidate)
    {
        var parts = new[]
        {
            candidate.Subject,
            candidate.Relation,
            candidate.Object,
            candidate.FactKey,
            candidate.SourceComponent,
            candidate.PayloadJson
        };

        return string.Join(
            " ",
            parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim()));
    }

    private static IReadOnlyDictionary<string, object?> BuildVectorPayload(
        GraphVectorRefreshJobDto job,
        GraphUpdateCandidateRecord candidate,
        string content)
    {
        return new Dictionary<string, object?>
        {
            ["doc_id"] = candidate.CandidateId,
            ["chunk_id"] = job.JobId,
            ["content"] = content,
            ["tenant_id"] = candidate.TenantId,
            ["user_id_hash"] = candidate.UserIdHash,
            ["node_id"] = candidate.NodeId,
            ["graph_candidate_id"] = candidate.CandidateId,
            ["recursive_event_id"] = candidate.RecursiveEventId,
            ["trace_id"] = candidate.TraceId,
            ["fact_key"] = candidate.FactKey,
            ["candidate_state"] = candidate.CandidateState,
            ["temporal_state"] = candidate.TemporalState,
            ["source_component"] = candidate.SourceComponent,
            ["source_hash"] = candidate.SourceHash,
            ["source_type"] = "graph_update_candidate",
            ["refresh_reason"] = job.RefreshReason
        };
    }

    private static string ResolveAffectedVectorScope(GraphUpdateCandidateRecord candidate)
    {
        return string.Equals(candidate.TemporalState, "current", StringComparison.OrdinalIgnoreCase)
            ? "graph_fact_current"
            : "graph_fact_non_current";
    }

    private static Guid BuildPointId(string jobId)
    {
        var hex = HashText(jobId)[..32];
        return Guid.Parse($"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}");
    }

    private static string NormalizeError(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown_error";
        }

        var trimmed = value.Trim();
        return trimmed.Length > 120 ? trimmed[..120] : trimmed;
    }

    private void EnsureProcessorDependencies()
    {
        if (_candidateRepository is null
            || _shard1 is null
            || _shard2 is null
            || _embeddingProvider is null
            || _options is null)
        {
            throw new InvalidOperationException("Graph vector refresh processing dependencies are not configured.");
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string HashText(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
#nullable disable
