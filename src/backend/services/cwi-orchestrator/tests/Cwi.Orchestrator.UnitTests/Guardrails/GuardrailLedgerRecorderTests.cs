using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Guardrails.Provenance;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Application.Guardrails.Scoring;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Guardrails;

public class GuardrailLedgerRecorderTests
{
    [Fact]
    public async Task RecordAsync_appends_ledger_event()
    {
        var repo = new FakeLedgerRepository();
        var recorder = new GuardrailLedgerRecorder(repo, new LedgerEventFactory(new NoopSignatureProvider()));

        await recorder.RecordAsync(
            traceId: "trace-1",
            flow: "ask",
            policyMode: "observe",
            actionType: null,
            modelId: "model-a",
            packHash: "pack-1",
            packVersion: "1.0.0",
            policyVersion: "v1",
            violations: new[] { new GuardrailViolation { Id = "quantity_bounds", Severity = "high" } },
            score: new SeverityScore(0.9, "critical"),
            decision: null,
            cancellationToken: CancellationToken.None);

        repo.LastEvent.Should().NotBeNull();
        repo.LastEvent!.EventType.Should().Be("guardrails.validation");
        repo.LastEvent.TraceId.Should().Be("trace-1");
        repo.LastEvent.PayloadJson.Should().Contain("Violations");
    }

    private sealed class FakeLedgerRepository : ILedgerRepository
    {
        public LedgerEvent? LastEvent { get; private set; }
        public string? LatestHash { get; set; }

        public Task<string?> GetLatestHashAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult(LatestHash);
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
            return Task.FromResult<IReadOnlyList<LedgerEvent>>(Array.Empty<LedgerEvent>());
        }

        public Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken)
        {
            LastEvent = ledgerEvent;
            return Task.CompletedTask;
        }
    }
}
