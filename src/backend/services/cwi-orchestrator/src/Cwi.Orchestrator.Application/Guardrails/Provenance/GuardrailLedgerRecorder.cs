using System.Text.Json;
using Cwi.Orchestrator.Application.Guardrails.Governance;
using Cwi.Orchestrator.Application.Guardrails.Scoring;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Guardrails.Provenance;

public sealed class GuardrailLedgerRecorder : IGuardrailLedgerRecorder
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;

    public GuardrailLedgerRecorder(ILedgerRepository ledgerRepository, LedgerEventFactory ledgerEventFactory)
    {
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
    }

    public async Task RecordAsync(
        string traceId,
        string flow,
        string? policyMode,
        string? actionType,
        string? modelId,
        string? packHash,
        string? packVersion,
        string? policyVersion,
        IReadOnlyList<GuardrailViolation> violations,
        SeverityScore score,
        GuardrailDecision? decision,
        CancellationToken cancellationToken)
    {
        var payload = new GuardrailLedgerPayload
        {
            TraceId = traceId,
            Flow = flow,
            PolicyMode = policyMode,
            ActionType = actionType,
            ModelId = modelId,
            PackHash = packHash,
            PackVersion = packVersion,
            PolicyVersion = policyVersion,
            Violations = violations,
            Score = score,
            Decision = decision,
            EvaluatedUtc = DateTimeOffset.UtcNow
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(payloadJson);
        var canonicalPayload = CanonicalJson.Canonicalize(document.RootElement);
        var prevHash = await _ledgerRepository.GetLatestHashAsync(traceId, cancellationToken).ConfigureAwait(false);
        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId,
            "guardrails.validation",
            canonicalPayload,
            prevHash ?? string.Empty);
        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }
}
