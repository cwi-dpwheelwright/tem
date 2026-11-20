#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Runtime;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Runtime;

public sealed class ResponseQualityGraphServiceTests
{
    [Fact]
    public async Task GetAsync_separates_active_and_resolved_quality_observations()
    {
        var repository = new FakeRecursiveEventRepository();
        repository.Events.Add(CreateQualityRecord(
            eventId: "quality-active-1",
            traceId: "trace-active",
            rule: "backend_language_in_everyday_answer",
            fixStatus: "active",
            temporalState: "current"));
        repository.Events.Add(CreateQualityRecord(
            eventId: "quality-resolved-1",
            traceId: "trace-resolved",
            rule: "backend_language_in_everyday_answer",
            fixStatus: "resolved",
            temporalState: "historical"));

        var service = new ResponseQualityGraphService(repository);

        var result = await service.GetAsync("tenant-1", page: 1, pageSize: 50, CancellationToken.None);

        result.ActiveIssues.Should().ContainSingle(issue => issue.EventId == "quality-active-1");
        result.ResolvedIssues.Should().ContainSingle(issue => issue.EventId == "quality-resolved-1");
    }

    [Fact]
    public async Task GetAsync_treats_historical_quality_observations_as_resolved_not_active()
    {
        var repository = new FakeRecursiveEventRepository();
        repository.Events.Add(CreateQualityRecord(
            eventId: "quality-historical-1",
            traceId: "trace-historical",
            rule: "message_draft_too_thin",
            fixStatus: "active",
            temporalState: "historical"));

        var service = new ResponseQualityGraphService(repository);

        var result = await service.GetAsync("tenant-1", page: 1, pageSize: 50, CancellationToken.None);

        result.ActiveIssues.Should().BeEmpty();
        result.ResolvedIssues.Should().ContainSingle(issue =>
            issue.EventId == "quality-historical-1"
            && issue.TemporalState == "historical");
    }

    [Fact]
    public async Task GetAsync_groups_recurring_failure_patterns()
    {
        var repository = new FakeRecursiveEventRepository();
        repository.Events.Add(CreateQualityRecord(
            eventId: "quality-pattern-1",
            traceId: "trace-1",
            rule: "backend_language_in_everyday_answer",
            fixStatus: "active",
            temporalState: "current",
            criticScore: 0.5d));
        repository.Events.Add(CreateQualityRecord(
            eventId: "quality-pattern-2",
            traceId: "trace-2",
            rule: "backend_language_in_everyday_answer",
            fixStatus: "active",
            temporalState: "current",
            criticScore: 0.7d));
        repository.Events.Add(CreateQualityRecord(
            eventId: "quality-other-1",
            traceId: "trace-3",
            rule: "message_draft_too_thin",
            fixStatus: "active",
            temporalState: "current"));

        var service = new ResponseQualityGraphService(repository);

        var result = await service.GetAsync("tenant-1", page: 1, pageSize: 50, CancellationToken.None);

        result.RecurringPatterns.Should().ContainSingle(pattern =>
            pattern.Rule == "backend_language_in_everyday_answer"
            && pattern.Route == "model_answer"
            && pattern.PromptType == "practical_guidance"
            && pattern.ActiveCount == 2
            && pattern.AverageCriticScore == 0.6d);
    }

    private static RecursiveEventRecord CreateQualityRecord(
        string eventId,
        string traceId,
        string rule,
        string fixStatus,
        string temporalState,
        double criticScore = 0.5d)
    {
        return new RecursiveEventRecord
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            SchemaVersion = "recursive-event.v1",
            EventType = "quality_observation.created",
            TenantId = "tenant-1",
            ScopeType = "user",
            UserIdHash = "user-hash-123456789",
            SourceComponent = "runtime_critic",
            SourceType = "critic",
            Confidence = 0.8d,
            TraceId = traceId,
            PolicyStatus = "observe_only",
            TemporalState = temporalState,
            TimestampUtc = DateTimeOffset.Parse("2026-05-06T12:00:00Z"),
            PayloadJson = JsonSerializer.Serialize(new
            {
                category = "synthesis",
                rule,
                severity = "warn",
                detail = "Synthetic test finding.",
                route = "model_answer",
                prompt_type = "practical_guidance",
                source_behavior = "source_state_consistent",
                critic_score = criticScore,
                fix_status = fixStatus
            }),
            ProvenanceJson = "{}",
            EventJson = "{}",
            SourceHash = "source-hash-123456",
            CreatedUtc = DateTimeOffset.Parse("2026-05-06T12:00:01Z")
        };
    }

    private sealed class FakeRecursiveEventRepository : IRecursiveEventRepository
    {
        public List<RecursiveEventRecord> Events { get; } = new();

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Events.Any(evt => evt.EventId == eventId));
        }

        public Task AddAsync(RecursiveEventRecord record, CancellationToken cancellationToken)
        {
            Events.Add(record);
            return Task.CompletedTask;
        }

        public Task AddQuarantineAsync(RecursiveEventQuarantineRecord record, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RecursiveEventRecord>> QueryAsync(
            string? tenantId,
            string? eventType,
            string? temporalState,
            int skip,
            int take,
            CancellationToken cancellationToken)
        {
            IEnumerable<RecursiveEventRecord> query = Events;
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                query = query.Where(evt => evt.TenantId == tenantId);
            }

            if (!string.IsNullOrWhiteSpace(eventType))
            {
                query = query.Where(evt => evt.EventType == eventType);
            }

            if (!string.IsNullOrWhiteSpace(temporalState))
            {
                query = query.Where(evt => evt.TemporalState == temporalState);
            }

            return Task.FromResult<IReadOnlyList<RecursiveEventRecord>>(
                query.Skip(skip).Take(take).ToList());
        }
    }
}
#nullable disable
