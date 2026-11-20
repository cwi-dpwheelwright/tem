#nullable enable
using System.Text.Json;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Inference;

public sealed class InferenceLedgerRecorder
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;

    public InferenceLedgerRecorder(ILedgerRepository ledgerRepository, LedgerEventFactory ledgerEventFactory)
    {
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
    }

    public async Task RecordAlignmentAsync(string traceId, MultiSourceAlignmentDto alignment, CancellationToken cancellationToken)
    {
        var payload = new
        {
            trace_id = traceId,
            alignment
        };

        await AppendAsync(traceId, "INFERENCE_ALIGNMENT_NORMALIZED", payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordDiscrepanciesAsync(
        string traceId,
        string severity,
        double score,
        IReadOnlyList<DiscrepancyRecord> discrepancies,
        IReadOnlyList<CorrectionDiff> corrections,
        bool fallbackApplied,
        CancellationToken cancellationToken)
    {
        var detectedPayload = new
        {
            trace_id = traceId,
            discrepancies
        };
        await AppendAsync(traceId, "DISCREPANCY_DETECTED", detectedPayload, cancellationToken).ConfigureAwait(false);

        var scoredPayload = new
        {
            trace_id = traceId,
            severity,
            score
        };
        await AppendAsync(traceId, "DISCREPANCY_SCORED", scoredPayload, cancellationToken).ConfigureAwait(false);

        var governedPayload = new
        {
            trace_id = traceId,
            action = fallbackApplied ? "fallback" : "review",
            severity
        };
        await AppendAsync(traceId, "DISCREPANCY_GOVERNED", governedPayload, cancellationToken).ConfigureAwait(false);

        if (corrections.Count > 0)
        {
            var correctionPayload = new
            {
                trace_id = traceId,
                corrections
            };
            await AppendAsync(traceId, "CORRECTIVE_OUTPUT_EMITTED", correctionPayload, cancellationToken)
                .ConfigureAwait(false);
        }

        if (fallbackApplied)
        {
            var fallbackPayload = new
            {
                trace_id = traceId,
                reason = "high_severity_discrepancy"
            };
            await AppendAsync(traceId, "FALLBACK_SELECTED", fallbackPayload, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RecordDiscrepancyApprovalRequestedAsync(
        string traceId,
        string ticketId,
        string severity,
        double score,
        string? reason,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            trace_id = traceId,
            ticket_id = ticketId,
            severity,
            score,
            reason
        };

        await AppendAsync(traceId, "DISCREPANCY_APPROVAL_REQUESTED", payload, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task AppendAsync(string traceId, string eventType, object payload, CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(payloadJson);
        var canonicalPayload = CanonicalJson.Canonicalize(document.RootElement);
        var prevHash = await _ledgerRepository.GetLatestHashAsync(traceId, cancellationToken).ConfigureAwait(false);
        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId,
            eventType,
            canonicalPayload,
            prevHash ?? string.Empty);
        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }
}
#nullable disable
