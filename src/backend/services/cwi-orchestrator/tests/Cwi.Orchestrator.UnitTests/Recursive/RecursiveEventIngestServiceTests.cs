#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Recursive;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Recursive;

public sealed class RecursiveEventIngestServiceTests
{
    [Fact]
    public async Task IngestAsync_accepts_valid_event_and_records_provenance()
    {
        var repository = new FakeRecursiveEventRepository();
        var ledger = new FakeLedgerRepository();
        var service = CreateService(repository, ledger);
        var json = File.ReadAllText(ResolveFixture("recursive-event.valid.json"));

        var result = await service.IngestAsync(json, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.Accepted.Should().BeTrue();
        repository.Events.Should().ContainSingle(evt => evt.EventId == "rec-evt-0001");
        repository.Quarantine.Should().BeEmpty();
        ledger.Events.Should().ContainSingle(evt => evt.EventType == "recursive_event.ingested");
    }

    [Fact]
    public async Task IngestAsync_quarantines_invalid_event_without_appending_event()
    {
        var repository = new FakeRecursiveEventRepository();
        var ledger = new FakeLedgerRepository();
        var service = CreateService(repository, ledger);
        var json = File.ReadAllText(ResolveFixture("recursive-event.invalid-missing-tenant.json"));

        var result = await service.IngestAsync(json, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(202);
        result.Data!.Accepted.Should().BeFalse();
        result.Data.Quarantined.Should().BeTrue();
        result.Data.Reason.Should().Be("schema_validation_failed");
        repository.Events.Should().BeEmpty();
        repository.Quarantine.Should().ContainSingle(record => record.Reason == "schema_validation_failed");
        ledger.Events.Should().ContainSingle(evt => evt.EventType == "recursive_event.quarantined");
    }

    [Fact]
    public async Task IngestAsync_quarantines_duplicate_event_id()
    {
        var repository = new FakeRecursiveEventRepository();
        var ledger = new FakeLedgerRepository();
        var service = CreateService(repository, ledger);

        var first = await service.IngestAsync(
            File.ReadAllText(ResolveFixture("recursive-event.valid.json")),
            CancellationToken.None);
        var duplicate = await service.IngestAsync(
            File.ReadAllText(ResolveFixture("recursive-event.duplicate.json")),
            CancellationToken.None);

        first.StatusCode.Should().Be(201);
        duplicate.StatusCode.Should().Be(202);
        duplicate.Data!.Reason.Should().Be("duplicate_event_id");
        repository.Events.Should().ContainSingle(evt => evt.EventId == "rec-evt-0001");
        repository.Quarantine.Should().ContainSingle(record => record.Reason == "duplicate_event_id");
        ledger.Events.Should().Contain(evt => evt.EventType == "recursive_event.ingested");
        ledger.Events.Should().Contain(evt => evt.EventType == "recursive_event.quarantined");
    }

    [Fact]
    public async Task IngestAsync_preserves_stale_temporal_state()
    {
        var repository = new FakeRecursiveEventRepository();
        var ledger = new FakeLedgerRepository();
        var service = CreateService(repository, ledger);
        var json = File.ReadAllText(ResolveFixture("recursive-event.stale.json"));

        var result = await service.IngestAsync(json, CancellationToken.None);

        result.StatusCode.Should().Be(201);
        result.Data!.TemporalState.Should().Be("stale");
        repository.Events.Should().ContainSingle(evt =>
            evt.EventId == "rec-evt-stale-0001"
            && evt.TemporalState == "stale"
            && evt.SourceComponent == "process_miner");
    }

    [Fact]
    public async Task IngestAsync_creates_graph_candidate_for_graph_eligible_recursive_event_when_wired()
    {
        var repository = new FakeRecursiveEventRepository();
        var ledger = new FakeLedgerRepository();
        var graphRepository = new FakeGraphUpdateCandidateRepository();
        var service = new RecursiveEventIngestService(
            new RecursiveEventSchemaValidator(),
            repository,
            ledger,
            new LedgerEventFactory(new NoopSignatureProvider()),
            new GraphEvolutionService(graphRepository));
        var json = File.ReadAllText(ResolveFixture("recursive-event.stale.json"));

        var result = await service.IngestAsync(json, CancellationToken.None);

        result.StatusCode.Should().Be(201);
        graphRepository.Candidates.Should().ContainSingle(candidate =>
            candidate.RecursiveEventId == "rec-evt-stale-0001"
            && candidate.TemporalState == "stale"
            && candidate.CandidateState == "add");
    }

    [Fact]
    public async Task QueryAsync_filters_by_tenant_type_and_temporal_state()
    {
        var repository = new FakeRecursiveEventRepository();
        var service = CreateService(repository, new FakeLedgerRepository());
        await service.IngestAsync(File.ReadAllText(ResolveFixture("recursive-event.valid.json")), CancellationToken.None);
        await service.IngestAsync(File.ReadAllText(ResolveFixture("recursive-event.stale.json")), CancellationToken.None);

        var results = await service.QueryAsync(
            "tenant-1",
            "memory_candidate.observed",
            "stale",
            page: 1,
            pageSize: 10,
            CancellationToken.None);

        results.Should().ContainSingle(record =>
            record.EventId == "rec-evt-stale-0001"
            && record.TemporalState == "stale");
    }

    private static RecursiveEventIngestService CreateService(
        IRecursiveEventRepository repository,
        ILedgerRepository ledgerRepository)
    {
        return new RecursiveEventIngestService(
            new RecursiveEventSchemaValidator(),
            repository,
            ledgerRepository,
            new LedgerEventFactory(new NoopSignatureProvider()));
    }

    private static string ResolveFixture(string fixtureName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "contracts",
                "fixtures",
                "orchestrator",
                fixtureName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find recursive event fixture {fixtureName} from {AppContext.BaseDirectory}.");
    }

    private sealed class FakeRecursiveEventRepository : IRecursiveEventRepository
    {
        public List<RecursiveEventRecord> Events { get; } = new();
        public List<RecursiveEventQuarantineRecord> Quarantine { get; } = new();

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
            Quarantine.Add(record);
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
            var query = Events.AsEnumerable();
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
                query
                    .OrderByDescending(evt => evt.TimestampUtc)
                    .ThenByDescending(evt => evt.CreatedUtc)
                    .Skip(skip)
                    .Take(take)
                    .ToList());
        }
    }

    private sealed class FakeLedgerRepository : ILedgerRepository
    {
        public List<LedgerEvent> Events { get; } = new();

        public Task<string?> GetLatestHashAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(Events.LastOrDefault(evt => evt.TraceId == traceId)?.Hash);
        }

        public Task<IReadOnlyList<LedgerEvent>> GetByTraceIdAsync(
            string traceId,
            int skip,
            int take,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LedgerEvent>>(
                Events.Where(evt => evt.TraceId == traceId).Skip(skip).Take(take).ToList());
        }

        public Task<int> GetCountAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Events.Count(evt => evt.TraceId == traceId));
        }

        public Task<IReadOnlyList<LedgerEvent>> SearchAsync(
            DateTimeOffset? startUtc,
            DateTimeOffset? endUtc,
            string? eventType,
            CancellationToken cancellationToken)
        {
            IEnumerable<LedgerEvent> query = Events;
            if (!string.IsNullOrWhiteSpace(eventType))
            {
                query = query.Where(evt => string.Equals(evt.EventType, eventType, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyList<LedgerEvent>>(query.ToList());
        }

        public Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
        {
            Events.Add(ledgerEvent);
            return Task.CompletedTask;
        }
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
}
#nullable disable
