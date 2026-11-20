#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Common;
using Cwi.Common.Events;
using Cwi.Common.Journey;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Application.Runtime;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Runtime;

public sealed class TruthEngineServiceTests
{
    [Fact]
    public void Evaluate_returns_conflicted_when_memory_conflicts_exist()
    {
        var context = CreateContext();
        context.MemoryConflictSummaries = new[]
        {
            new MemoryConflictSummary
            {
                ConflictId = "conflict-1",
                Slot = "owner",
                MemoryIds = new[] { "m1", "m2" }
            }
        };

        var decision = new TruthEngineService().Evaluate(context, SelectiveEnforcementMode.Observe);

        decision.State.Should().Be("conflicted");
        decision.ConflictCount.Should().Be(1);
        decision.Mode.Should().Be("observe");
    }

    [Fact]
    public void Evaluate_returns_stale_when_only_stale_context_exists()
    {
        var context = CreateContext();
        context.MemoryFrameworkDecisions = new[]
        {
            new MemoryFrameworkDecision
            {
                MemoryId = "m1",
                Outcome = "excluded",
                TemporalState = "stale"
            }
        };

        var decision = new TruthEngineService().Evaluate(context, SelectiveEnforcementMode.Observe);

        decision.State.Should().Be("stale");
        decision.Reason.Should().Be("only_stale_or_superseded_context_available");
    }

    [Fact]
    public void Evaluate_returns_historical_when_only_historical_context_exists()
    {
        var context = CreateContext();
        context.MemoryFrameworkDecisions = new[]
        {
            new MemoryFrameworkDecision
            {
                MemoryId = "m1",
                Outcome = "excluded",
                TemporalState = "historical"
            }
        };

        var decision = new TruthEngineService().Evaluate(context, SelectiveEnforcementMode.Observe);

        decision.State.Should().Be("historical");
    }

    [Fact]
    public void Evaluate_returns_unsupported_when_required_evidence_is_missing()
    {
        var context = CreateContext();
        context.EvidenceRequired = true;

        var decision = new TruthEngineService().Evaluate(context, SelectiveEnforcementMode.Observe);

        decision.State.Should().Be("unsupported");
        decision.Reason.Should().Be("required_evidence_missing");
    }

    [Fact]
    public void Evaluate_returns_current_when_active_evidence_exists()
    {
        var context = CreateContext();
        context.GraphEvidence.Add(new GraphEvidence
        {
            Subject = "CWI",
            Relation = "has",
            Object = "memory framework",
            Template = "test"
        });

        var decision = new TruthEngineService().Evaluate(context, SelectiveEnforcementMode.Observe);

        decision.State.Should().Be("current");
        decision.EvidenceKinds.Should().Contain("graph");
    }

    [Fact]
    public void Evaluate_returns_estimated_for_non_grounded_general_reasoning()
    {
        var context = CreateContext();
        context.QueryClassification = "general_reasoning";
        context.GroundingMode = "non_grounded";

        var decision = new TruthEngineService().Evaluate(context, SelectiveEnforcementMode.Observe);

        decision.State.Should().Be("estimated");
    }

    [Fact]
    public async Task Step_records_truth_decision_in_context_and_ledger_without_changing_response()
    {
        var ledger = new FakeLedgerRepository();
        var step = new AskTruthEngineStep(
            new TruthEngineService(),
            new TruthEngineLedgerRecorder(
                ledger,
                new LedgerEventFactory(new NoopSignatureProvider())),
            new SelectiveEnforcementOptions(),
            CreateNoopJourneyEmitter(),
            NullLogger<AskTruthEngineStep>.Instance);
        var context = CreateContext();
        context.TraceId = "trace-truth";
        context.EvidenceRequired = true;
        var response = new AskResponseDto
        {
            Answer = "Existing answer",
            Trace = new TracePayload
            {
                TraceId = "trace-truth",
                PolicyMode = "observe"
            }
        };

        var result = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(response)),
            CancellationToken.None);

        result.Data!.Answer.Should().Be("Existing answer");
        context.TruthState.Should().Be("unsupported");
        context.TruthEngineVersion.Should().Be(TruthEngineService.Version);
        ledger.Events.Should().ContainSingle(evt => evt.EventType == TruthEngineLedgerRecorder.EventType);
        var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(ledger.Events.Single().PayloadJson);
        payload!["event"]!.ToString().Should().Be("TRUTH_ENGINE_OBSERVED");
        payload["truth_state"]!.ToString().Should().Be("unsupported");
    }

    private static AskWorkflowContext CreateContext()
    {
        return new AskWorkflowContext("tenant-1", "user-1", "What should I do?", "pack", null, "observe", 3)
        {
            QueryClassification = "general_model_knowledge",
            GroundingMode = "source_grounded",
            ModelName = "model-1",
            PackHash = "pack-hash"
        };
    }

    private static JourneyEmitter CreateNoopJourneyEmitter()
    {
        var options = new EventBusClientOptions
        {
            Enabled = false,
            ServiceName = "test",
            Environment = "test"
        };
        var publisher = new JourneyEventBusPublisher(new EventBusClient(new HttpClient(), options), options);
        return new JourneyEmitter(new JourneyRequestContext(), publisher);
    }

    private sealed class FakeLedgerRepository : ILedgerRepository
    {
        public List<LedgerEvent> Events { get; } = new();

        public Task<string?> GetLatestHashAsync(string traceId, CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(Events.LastOrDefault(evt => evt.TraceId == traceId)?.Hash);
        }

        public Task<IReadOnlyList<LedgerEvent>> GetByTraceIdAsync(string traceId, int skip, int take, CancellationToken cancellationToken)
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
}
#nullable disable
