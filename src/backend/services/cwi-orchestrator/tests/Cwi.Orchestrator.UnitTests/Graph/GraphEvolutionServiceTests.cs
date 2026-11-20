#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Graph;

public sealed class GraphEvolutionServiceTests
{
    [Fact]
    public async Task CreateCandidateAsync_converts_current_observation_to_add_candidate()
    {
        var repository = new FakeGraphUpdateCandidateRepository();
        var service = new GraphEvolutionService(repository);

        var result = await service.CreateCandidateAsync(
            CreateEvent(
                eventId: "event-add",
                payloadJson: "{\"subject\":\"Project Polaris\",\"relation\":\"status\",\"object\":\"active\"}"),
            CancellationToken.None);

        result.Created.Should().BeTrue();
        repository.Candidates.Should().ContainSingle(candidate =>
            candidate.RecursiveEventId == "event-add"
            && candidate.CandidateState == "add"
            && candidate.TemporalState == "current"
            && candidate.Subject == "Project Polaris"
            && candidate.Relation == "status"
            && candidate.Object == "active");
    }

    [Fact]
    public async Task CreateCandidateAsync_preserves_conflicts_without_overwriting()
    {
        var repository = new FakeGraphUpdateCandidateRepository();
        var service = new GraphEvolutionService(repository);

        var result = await service.CreateCandidateAsync(
            CreateEvent(
                eventId: "event-conflict",
                temporalState: "conflicted",
                payloadJson: "{\"subject\":\"dashboard delivery\",\"relation\":\"owner\",\"object\":\"conflict: Riley or Morgan\"}"),
            CancellationToken.None);

        result.Created.Should().BeTrue();
        repository.Candidates.Should().ContainSingle(candidate =>
            candidate.CandidateState == "conflict"
            && candidate.TemporalState == "conflicted"
            && candidate.Object.Contains("Riley or Morgan", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateCandidateAsync_marks_historical_observations_as_historical_not_current()
    {
        var repository = new FakeGraphUpdateCandidateRepository();
        var service = new GraphEvolutionService(repository);

        await service.CreateCandidateAsync(
            CreateEvent(
                eventId: "event-current",
                payloadJson: "{\"subject\":\"web toggle\",\"relation\":\"state\",\"object\":\"enabled\"}"),
            CancellationToken.None);
        await service.CreateCandidateAsync(
            CreateEvent(
                eventId: "event-historical",
                temporalState: "historical",
                payloadJson: "{\"subject\":\"web toggle\",\"relation\":\"state\",\"object\":\"disabled\"}"),
            CancellationToken.None);

        var current = await service.QueryAsync("tenant-1", null, "current", 1, 10, CancellationToken.None);
        var historical = await service.QueryAsync("tenant-1", null, "historical", 1, 10, CancellationToken.None);

        current.Should().ContainSingle(candidate => candidate.Object == "enabled");
        historical.Should().ContainSingle(candidate => candidate.Object == "disabled");
    }

    [Theory]
    [InlineData("{\"subject\":\"priority\",\"relation\":\"state\",\"object\":\"changed\",\"operation\":\"update\"}", "update")]
    [InlineData("{\"subject\":\"priority\",\"relation\":\"state\",\"object\":\"old\",\"supersedes_fact_key\":\"priority.state\"}", "supersede")]
    public async Task CreateCandidateAsync_supports_update_and_supersede_states(
        string payloadJson,
        string expectedState)
    {
        var repository = new FakeGraphUpdateCandidateRepository();
        var service = new GraphEvolutionService(repository);

        var result = await service.CreateCandidateAsync(
            CreateEvent(eventId: $"event-{expectedState}", payloadJson: payloadJson),
            CancellationToken.None);

        result.Created.Should().BeTrue();
        repository.Candidates.Should().ContainSingle(candidate => candidate.CandidateState == expectedState);
    }

    [Fact]
    public async Task CreateCandidateAsync_is_idempotent_per_recursive_event()
    {
        var repository = new FakeGraphUpdateCandidateRepository();
        var service = new GraphEvolutionService(repository);
        var evt = CreateEvent(
            eventId: "event-idempotent",
            payloadJson: "{\"subject\":\"open loop\",\"relation\":\"status\",\"object\":\"active\"}");

        var first = await service.CreateCandidateAsync(evt, CancellationToken.None);
        var second = await service.CreateCandidateAsync(evt, CancellationToken.None);

        first.Created.Should().BeTrue();
        second.Skipped.Should().BeTrue();
        second.Reason.Should().Be("candidate_already_exists");
        repository.Candidates.Should().ContainSingle(candidate => candidate.RecursiveEventId == "event-idempotent");
    }

    [Fact]
    public async Task CreateCandidateAsync_emits_vector_refresh_job_when_refresh_service_is_available()
    {
        var repository = new FakeGraphUpdateCandidateRepository();
        var refreshRepository = new FakeGraphVectorRefreshJobRepository();
        var service = new GraphEvolutionService(
            repository,
            new GraphToVectorRefreshService(refreshRepository));

        var result = await service.CreateCandidateAsync(
            CreateEvent(
                eventId: "event-refresh",
                payloadJson: "{\"subject\":\"priority\",\"relation\":\"state\",\"object\":\"current\"}"),
            CancellationToken.None);

        result.Created.Should().BeTrue();
        result.VectorRefreshJob.Should().NotBeNull();
        refreshRepository.Jobs.Should().ContainSingle(job =>
            job.GraphCandidateId == result.Candidate!.CandidateId
            && job.RefreshStatus == "pending"
            && job.FactKey == result.Candidate.FactKey);
    }

    private static RecursiveEventRecord CreateEvent(
        string eventId,
        string payloadJson,
        string temporalState = "current")
    {
        return new RecursiveEventRecord
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            SchemaVersion = "recursive-event.v1",
            EventType = "memory_candidate.observed",
            TenantId = "tenant-1",
            ScopeType = "user",
            UserIdHash = "user-hash-123456789",
            SourceComponent = "process_miner",
            SourceType = "process_miner",
            Confidence = 0.82d,
            TraceId = $"trace-{eventId}",
            PolicyStatus = "observe_only",
            TemporalState = temporalState,
            TimestampUtc = DateTimeOffset.Parse("2026-05-06T12:00:00Z"),
            PayloadJson = payloadJson,
            ProvenanceJson = "{\"source_hash\":\"source-hash-123456\",\"created_utc\":\"2026-05-06T12:00:01Z\"}",
            EventJson = "{}",
            SourceHash = "source-hash-123456",
            CreatedUtc = DateTimeOffset.Parse("2026-05-06T12:00:01Z")
        };
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
}
#nullable disable
