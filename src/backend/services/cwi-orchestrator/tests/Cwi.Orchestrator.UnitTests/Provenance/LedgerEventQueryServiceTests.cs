using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Provenance;

public sealed class LedgerEventQueryServiceTests
{
    [Fact]
    public async Task QueryAsync_returns_empty_when_start_after_end()
    {
        var repo = new FakeLedgerRepository(Array.Empty<LedgerEvent>());
        var service = new LedgerEventQueryService(repo);

        var result = await service.QueryAsync(
            startUtc: DateTimeOffset.UtcNow,
            endUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            tenantId: null,
            eventType: null,
            page: 1,
            pageSize: 10,
            cancellationToken: CancellationToken.None);

        result.Total.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_filters_by_event_type_and_tenant()
    {
        var now = DateTimeOffset.UtcNow;
        var events = new List<LedgerEvent>
        {
            new LedgerEvent
            {
                Id = Guid.NewGuid(),
                TraceId = "trace-a",
                EventType = "break_glass.issued",
                PayloadJson = "{\"tenant_id\":\"tenant-a\"}",
                CreatedUtc = now
            },
            new LedgerEvent
            {
                Id = Guid.NewGuid(),
                TraceId = "trace-b",
                EventType = "break_glass.issued",
                PayloadJson = "{\"tenant_id\":\"tenant-b\"}",
                CreatedUtc = now
            },
            new LedgerEvent
            {
                Id = Guid.NewGuid(),
                TraceId = "trace-c",
                EventType = "pack.activation",
                PayloadJson = "{\"tenant_id\":\"tenant-a\"}",
                CreatedUtc = now
            }
        };

        var repo = new FakeLedgerRepository(events);
        var service = new LedgerEventQueryService(repo);

        var result = await service.QueryAsync(
            startUtc: null,
            endUtc: null,
            tenantId: "tenant-a",
            eventType: "break_glass.issued",
            page: 1,
            pageSize: 10,
            cancellationToken: CancellationToken.None);

        result.Total.Should().Be(1);
        result.Items.Should().HaveCount(1);
        result.Items[0].TraceId.Should().Be("trace-a");
    }

    [Fact]
    public async Task QueryAsync_caps_page_size_and_paginates()
    {
        var now = DateTimeOffset.UtcNow;
        var events = new List<LedgerEvent>();
        for (var i = 0; i < 250; i++)
        {
            events.Add(new LedgerEvent
            {
                Id = Guid.NewGuid(),
                TraceId = $"trace-{i}",
                EventType = "evt",
                PayloadJson = "{\"tenant_id\":\"tenant-a\"}",
                CreatedUtc = now.AddSeconds(-i)
            });
        }

        var repo = new FakeLedgerRepository(events);
        var service = new LedgerEventQueryService(repo);

        var result = await service.QueryAsync(
            startUtc: null,
            endUtc: null,
            tenantId: "tenant-a",
            eventType: "evt",
            page: 1,
            pageSize: 500,
            cancellationToken: CancellationToken.None);

        result.PageSize.Should().Be(200);
        result.Total.Should().Be(250);
        result.Items.Should().HaveCount(200);
    }

    private sealed class FakeLedgerRepository : ILedgerRepository
    {
        private readonly IReadOnlyList<LedgerEvent> _events;

        public FakeLedgerRepository(IReadOnlyList<LedgerEvent> events)
        {
            _events = events;
        }

        public Task<string?> GetLatestHashAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<IReadOnlyList<LedgerEvent>> GetByTraceIdAsync(string traceId, int skip, int take, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<LedgerEvent>>(Array.Empty<LedgerEvent>());
        }

        public Task<int> GetCountAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        public Task<IReadOnlyList<LedgerEvent>> SearchAsync(
            DateTimeOffset? startUtc,
            DateTimeOffset? endUtc,
            string? eventType,
            CancellationToken cancellationToken)
        {
            IEnumerable<LedgerEvent> query = _events;

            if (startUtc.HasValue)
            {
                query = query.Where(evt => evt.CreatedUtc >= startUtc.Value);
            }

            if (endUtc.HasValue)
            {
                query = query.Where(evt => evt.CreatedUtc <= endUtc.Value);
            }

            if (!string.IsNullOrWhiteSpace(eventType))
            {
                query = query.Where(evt => string.Equals(evt.EventType, eventType, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyList<LedgerEvent>>(query.ToList());
        }

        public Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
