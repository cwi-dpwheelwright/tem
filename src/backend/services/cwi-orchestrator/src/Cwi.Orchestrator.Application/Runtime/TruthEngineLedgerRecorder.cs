#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Runtime;

public sealed class TruthEngineLedgerRecorder
{
    public const string EventType = "truth_engine.observed";

    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _factory;

    public TruthEngineLedgerRecorder(
        ILedgerRepository ledgerRepository,
        LedgerEventFactory factory)
    {
        _ledgerRepository = ledgerRepository;
        _factory = factory;
    }

    public async Task RecordAsync(
        AskWorkflowContext context,
        TruthEngineDecisionDto decision,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.TraceId))
        {
            return;
        }

        var previousHash = await _ledgerRepository
            .GetLatestHashAsync(context.TraceId, cancellationToken)
            .ConfigureAwait(false) ?? string.Empty;

        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["event"] = "TRUTH_ENGINE_OBSERVED",
            ["version"] = decision.Version,
            ["mode"] = decision.Mode,
            ["tenant_id"] = context.TenantId,
            ["user_id"] = context.UserId,
            ["request_id"] = context.RequestId,
            ["trace_id"] = context.TraceId,
            ["truth_state"] = decision.State,
            ["truth_reason"] = decision.Reason,
            ["evidence_count"] = decision.EvidenceCount,
            ["conflict_count"] = decision.ConflictCount,
            ["evidence_kinds"] = decision.EvidenceKinds,
            ["query_classification"] = context.QueryClassification,
            ["intent_type"] = context.IntentType,
            ["grounding_mode"] = context.GroundingMode,
            ["evidence_required"] = context.EvidenceRequired,
            ["retrieval_skipped_reason"] = context.RetrievalSkippedReason,
            ["evaluated_utc"] = decision.EvaluatedUtc
        });

        var ledgerEvent = _factory.CreateSignedEvent(
            context.TraceId,
            EventType,
            payload,
            previousHash);

        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }
}
#nullable disable
