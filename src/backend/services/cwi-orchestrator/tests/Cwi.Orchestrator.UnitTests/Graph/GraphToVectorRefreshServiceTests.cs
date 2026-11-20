#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Retrieval;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Graph;

public sealed class GraphToVectorRefreshServiceTests
{
    [Fact]
    public async Task CreateRefreshJobAsync_creates_pending_idempotent_job_for_graph_candidate()
    {
        var repository = new FakeGraphVectorRefreshJobRepository();
        var service = new GraphToVectorRefreshService(repository);
        var candidate = CreateCandidate("candidate-1");

        var first = await service.CreateRefreshJobAsync(candidate, CancellationToken.None);
        var second = await service.CreateRefreshJobAsync(candidate, CancellationToken.None);

        first.Created.Should().BeTrue();
        first.Job!.RefreshStatus.Should().Be(GraphVectorRefreshStatuses.Pending);
        first.Job.GraphCandidateId.Should().Be("candidate-1");
        second.Skipped.Should().BeTrue();
        second.Reason.Should().Be("refresh_job_already_exists");
        repository.Jobs.Should().ContainSingle(job => job.GraphCandidateId == "candidate-1");
    }

    [Fact]
    public async Task QueryAsync_returns_jobs_by_status_and_candidate()
    {
        var repository = new FakeGraphVectorRefreshJobRepository();
        var service = new GraphToVectorRefreshService(repository);
        await service.CreateRefreshJobAsync(CreateCandidate("candidate-a"), CancellationToken.None);
        await service.CreateRefreshJobAsync(CreateCandidate("candidate-b"), CancellationToken.None);
        var candidateB = repository.Jobs.Single(job => job.GraphCandidateId == "candidate-b");
        candidateB.RefreshStatus = GraphVectorRefreshStatuses.Failed;
        candidateB.LastError = "vector shard unavailable";

        var failed = await service.QueryAsync(
            "tenant-1",
            GraphVectorRefreshStatuses.Failed,
            null,
            1,
            10,
            CancellationToken.None);
        var candidateA = await service.QueryAsync(
            "tenant-1",
            null,
            "candidate-a",
            1,
            10,
            CancellationToken.None);

        failed.Should().ContainSingle(job => job.GraphCandidateId == "candidate-b");
        candidateA.Should().ContainSingle(job => job.GraphCandidateId == "candidate-a");
    }

    [Fact]
    public async Task Status_transitions_expose_retry_and_failure_visibility()
    {
        var repository = new FakeGraphVectorRefreshJobRepository();
        var service = new GraphToVectorRefreshService(repository);
        var created = await service.CreateRefreshJobAsync(CreateCandidate("candidate-retry"), CancellationToken.None);

        var running = await service.MarkRunningAsync(created.Job!.JobId, CancellationToken.None);
        var failed = await service.MarkFailedAsync(created.Job.JobId, "qdrant_timeout", CancellationToken.None);
        var completed = await service.MarkCompletedAsync(created.Job.JobId, CancellationToken.None);

        running!.RefreshStatus.Should().Be(GraphVectorRefreshStatuses.Running);
        running.AttemptCount.Should().Be(1);
        failed!.RefreshStatus.Should().Be(GraphVectorRefreshStatuses.Failed);
        failed.LastError.Should().Be("qdrant_timeout");
        completed!.RefreshStatus.Should().Be(GraphVectorRefreshStatuses.Completed);
        completed.LastError.Should().BeNull();
        completed.CompletedUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessPendingAsync_upserts_graph_candidate_to_vector_shards_and_marks_completed()
    {
        var jobRepository = new FakeGraphVectorRefreshJobRepository();
        var candidateRepository = new FakeGraphUpdateCandidateRepository();
        var shard1Handler = new VectorUpsertCaptureHandler();
        var shard2Handler = new VectorUpsertCaptureHandler();
        var service = new GraphToVectorRefreshService(
            jobRepository,
            candidateRepository,
            new VectorShard1Client(new HttpClient(shard1Handler) { BaseAddress = new Uri("http://vector-1.local/") }),
            new VectorShard2Client(new HttpClient(shard2Handler) { BaseAddress = new Uri("http://vector-2.local/") }),
            new HashEmbeddingProvider(4),
            CreateOptions());
        var candidate = CreateCandidate("candidate-vector");
        candidate.Subject = "dashboard telemetry contract";
        candidate.Relation = "owned_by";
        candidate.Object = "PlatformOps";
        candidateRepository.Candidates.Add(candidate);
        await service.CreateRefreshJobAsync(candidate, CancellationToken.None);

        var result = await service.ProcessPendingAsync("tenant-1", 10, CancellationToken.None);

        result.Attempted.Should().Be(1);
        result.Completed.Should().Be(1);
        result.Failed.Should().Be(0);
        result.Jobs.Should().ContainSingle(job => job.RefreshStatus == GraphVectorRefreshStatuses.Completed);
        jobRepository.Jobs.Single().AttemptCount.Should().Be(1);
        shard1Handler.UpsertBodies.Should().ContainSingle(body => body.Contains("candidate-vector", StringComparison.Ordinal));
        shard2Handler.UpsertBodies.Should().ContainSingle(body => body.Contains("candidate-vector", StringComparison.Ordinal));
    }

    private static GraphUpdateCandidateRecord CreateCandidate(string candidateId)
    {
        return new GraphUpdateCandidateRecord
        {
            Id = Guid.NewGuid(),
            CandidateId = candidateId,
            TenantId = "tenant-1",
            UserIdHash = "user-hash",
            NodeId = "node-1",
            RecursiveEventId = $"event-{candidateId}",
            TraceId = $"trace-{candidateId}",
            CandidateState = "add",
            TemporalState = "current",
            Subject = "Project",
            Relation = "status",
            Object = "active",
            FactKey = $"fact-{candidateId}",
            Confidence = 0.9d,
            SourceHash = $"hash-{candidateId}",
            ObservedUtc = DateTimeOffset.Parse("2026-05-06T12:00:00Z"),
            CreatedUtc = DateTimeOffset.Parse("2026-05-06T12:00:01Z"),
            UpdatedUtc = DateTimeOffset.Parse("2026-05-06T12:00:01Z")
        };
    }

    private static ApplicationOptions CreateOptions()
    {
        return new ApplicationOptions(
            packsBaseUrl: "http://packs.local",
            packsDirectory: null,
            graphEnabled: true,
            graphDbConnectionString: null,
            graphName: "cwi_graph",
            graphQueryTimeoutSeconds: 5,
            graphTopK: 5,
            vectorShard1Url: "http://vector-1.local",
            vectorShard2Url: "http://vector-2.local",
            vectorEmbeddingDimension: 4,
            modelBaseUrl: "http://model.local",
            modelName: "test-model",
            modelTimeoutSeconds: 30,
            policyVersion: "policy-v1",
            ledgerHashSuiteId: "ledger-v1");
    }

    private sealed class FakeGraphUpdateCandidateRepository : IGraphUpdateCandidateRepository
    {
        public List<GraphUpdateCandidateRecord> Candidates { get; } = new();

        public Task<bool> ExistsForRecursiveEventAsync(
            string recursiveEventId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Candidates.Any(candidate => candidate.RecursiveEventId == recursiveEventId));
        }

        public Task AddAsync(
            GraphUpdateCandidateRecord record,
            CancellationToken cancellationToken)
        {
            Candidates.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<GraphUpdateCandidateRecord>> QueryAsync(
            string? tenantId,
            string? candidateState,
            string? temporalState,
            int skip,
            int take,
            CancellationToken cancellationToken)
        {
            IEnumerable<GraphUpdateCandidateRecord> query = Candidates;
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                query = query.Where(candidate => candidate.TenantId == tenantId);
            }

            if (!string.IsNullOrWhiteSpace(candidateState))
            {
                query = query.Where(candidate => candidate.CandidateState == candidateState);
            }

            if (!string.IsNullOrWhiteSpace(temporalState))
            {
                query = query.Where(candidate => candidate.TemporalState == temporalState);
            }

            return Task.FromResult<IReadOnlyList<GraphUpdateCandidateRecord>>(
                query.Skip(skip).Take(take).ToList());
        }

        public Task<GraphUpdateCandidateRecord?> GetByCandidateIdAsync(
            string candidateId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Candidates.SingleOrDefault(candidate => candidate.CandidateId == candidateId));
        }
    }

    private sealed class FakeGraphVectorRefreshJobRepository : IGraphVectorRefreshJobRepository
    {
        public List<GraphVectorRefreshJobRecord> Jobs { get; } = new();

        public Task<bool> ExistsForGraphCandidateAsync(
            string graphCandidateId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Jobs.Any(job => job.GraphCandidateId == graphCandidateId));
        }

        public Task AddAsync(
            GraphVectorRefreshJobRecord record,
            CancellationToken cancellationToken)
        {
            Jobs.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<GraphVectorRefreshJobRecord>> QueryAsync(
            string? tenantId,
            string? refreshStatus,
            string? graphCandidateId,
            int skip,
            int take,
            CancellationToken cancellationToken)
        {
            IEnumerable<GraphVectorRefreshJobRecord> query = Jobs;
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                query = query.Where(job => job.TenantId == tenantId);
            }

            if (!string.IsNullOrWhiteSpace(refreshStatus))
            {
                query = query.Where(job => job.RefreshStatus == refreshStatus);
            }

            if (!string.IsNullOrWhiteSpace(graphCandidateId))
            {
                query = query.Where(job => job.GraphCandidateId == graphCandidateId);
            }

            return Task.FromResult<IReadOnlyList<GraphVectorRefreshJobRecord>>(
                query.Skip(skip).Take(take).ToList());
        }

        public Task<GraphVectorRefreshJobRecord?> GetByJobIdAsync(
            string jobId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Jobs.SingleOrDefault(job => job.JobId == jobId));
        }

        public Task UpdateAsync(
            GraphVectorRefreshJobRecord record,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class VectorUpsertCaptureHandler : HttpMessageHandler
    {
        public List<string> UpsertBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.PathAndQuery.Contains("/points", StringComparison.OrdinalIgnoreCase) is true)
            {
                UpsertBodies.Add(request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":true}", Encoding.UTF8, "application/json")
            };
        }
    }
}
#nullable disable
