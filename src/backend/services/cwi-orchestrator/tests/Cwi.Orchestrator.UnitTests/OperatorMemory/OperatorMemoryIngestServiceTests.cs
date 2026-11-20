#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.OperatorMemory;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.OperatorMemory;

public class OperatorMemoryIngestServiceTests
{
    [Fact]
    public async Task Ingest_rejects_without_consent()
    {
        var options = new OperatorMemoryOptions();
        var repo = new InMemoryOperatorProfileRepository();
        var rateLimiter = new OperatorMemoryRateLimiter(options);
        var redaction = new OperatorMemoryRedactionPolicy(options);
        var ledger = new InMemoryLedgerRepository();
        var service = new OperatorMemoryIngestService(
            repo,
            redaction,
            rateLimiter,
            options,
            ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));

        var response = await service.HandleAsync(new OperatorMemoryIngestRequestDto
        {
            TenantId = "tenant",
            OperatorId = "operator",
            Signals = new List<OperatorSignalDto>
            {
                new()
                {
                    SignalType = "correction",
                    Value = 1,
                    Unit = "count",
                    OccurredUtc = System.DateTimeOffset.UtcNow
                }
            }
        }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(403);
        ledger.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Ingest_records_ledger_event_and_profile()
    {
        var options = new OperatorMemoryOptions();
        var repo = new InMemoryOperatorProfileRepository();
        var rateLimiter = new OperatorMemoryRateLimiter(options);
        var redaction = new OperatorMemoryRedactionPolicy(options);
        var ledger = new InMemoryLedgerRepository();
        var service = new OperatorMemoryIngestService(
            repo,
            redaction,
            rateLimiter,
            options,
            ledger,
            new LedgerEventFactory(new NoopSignatureProvider()));

        var response = await service.HandleAsync(new OperatorMemoryIngestRequestDto
        {
            TenantId = "tenant",
            OperatorId = "operator",
            Consent = new OperatorConsentDto { Granted = true },
            Signals = new List<OperatorSignalDto>
            {
                new()
                {
                    SignalType = "correction",
                    Value = 1,
                    Unit = "count",
                    OccurredUtc = System.DateTimeOffset.UtcNow
                },
                new()
                {
                    SignalType = "confirmation",
                    Value = 1,
                    Unit = "count",
                    OccurredUtc = System.DateTimeOffset.UtcNow
                }
            },
            Performance = new List<OperatorPerformanceDto>
            {
                new()
                {
                    Metric = "response_latency_ms",
                    Value = 400,
                    Unit = "ms",
                    WindowUtc = System.DateTimeOffset.UtcNow
                }
            }
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Accepted.Should().Be(3);
        response.Data.Profile.Should().NotBeNull();
        ledger.Events.Should().ContainSingle(e => e.EventType == "operator_memory.ingest_batch");
    }

    private sealed class InMemoryLedgerRepository : ILedgerRepository
    {
        public List<LedgerEvent> Events { get; } = new();

        public Task<string?> GetLatestHashAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(Events.Count == 0 ? null : Events[^1].Hash);
        }

        public Task<IReadOnlyList<LedgerEvent>> GetByTraceIdAsync(string traceId, int skip, int take, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LedgerEvent>>(Events);
        }

        public Task<int> GetCountAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Events.Count);
        }

        public Task<IReadOnlyList<LedgerEvent>> SearchAsync(System.DateTimeOffset? startUtc, System.DateTimeOffset? endUtc, string? eventType, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LedgerEvent>>(Events);
        }

        public Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
        {
            Events.Add(ledgerEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryOperatorProfileRepository : IOperatorProfileRepository
    {
        private readonly Dictionary<string, OperatorProfile> _profiles = new();

        public Task<OperatorProfile?> GetAsync(string tenantId, string operatorId, CancellationToken cancellationToken)
        {
            var key = BuildKey(tenantId, operatorId);
            _profiles.TryGetValue(key, out var profile);
            return Task.FromResult(profile);
        }

        public Task UpsertAsync(OperatorProfile profile, CancellationToken cancellationToken)
        {
            var key = BuildKey(profile.TenantId, profile.OperatorId);
            _profiles[key] = profile;
            return Task.CompletedTask;
        }

        private static string BuildKey(string tenantId, string operatorId)
        {
            return $"{tenantId.Trim().ToLowerInvariant()}::{operatorId.Trim().ToLowerInvariant()}";
        }
    }
}
#nullable disable
