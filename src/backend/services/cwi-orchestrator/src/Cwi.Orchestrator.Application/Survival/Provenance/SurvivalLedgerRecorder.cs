#nullable enable
using System.Text.Json;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Survival.Provenance;

public sealed class SurvivalLedgerRecorder
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;

    public SurvivalLedgerRecorder(ILedgerRepository ledgerRepository, LedgerEventFactory ledgerEventFactory)
    {
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
    }

    public async Task RecordTraceAsync(
        string traceId,
        string? mode,
        string? profile,
        DateTimeOffset? lastSignalUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(traceId))
        {
            return;
        }

        if (!string.Equals(mode, "survival", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var payload = new
        {
            trace_id = traceId,
            mode,
            profile,
            last_signal_utc = lastSignalUtc
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(payloadJson);
        var canonicalPayload = CanonicalJson.Canonicalize(document.RootElement);
        var prevHash = await _ledgerRepository.GetLatestHashAsync(traceId, cancellationToken).ConfigureAwait(false);
        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId,
            "SURVIVAL_STATUS_RECORDED",
            canonicalPayload,
            prevHash ?? string.Empty);
        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }
}
#nullable disable
