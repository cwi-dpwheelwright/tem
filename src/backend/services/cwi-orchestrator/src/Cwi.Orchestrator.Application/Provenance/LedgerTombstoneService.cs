using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Provenance;

public sealed class LedgerTombstoneService
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;
    private readonly LedgerRedactionPolicy? _redactionPolicy;

    public LedgerTombstoneService(
        ILedgerRepository ledgerRepository,
        LedgerEventFactory ledgerEventFactory,
        LedgerRedactionPolicy? redactionPolicy)
    {
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
        _redactionPolicy = redactionPolicy;
    }

    public async Task TombstoneAsync(string traceId, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(traceId))
        {
            throw new ArgumentException("traceId required", nameof(traceId));
        }

        var payload = new
        {
            trace_id = traceId,
            reason = reason,
            redacted_fields = _redactionPolicy?.Fields ?? Array.Empty<string>()
        };

        var payloadJson = System.Text.Json.JsonSerializer.Serialize(payload);
        var prevHash = await _ledgerRepository.GetLatestHashAsync(traceId, cancellationToken).ConfigureAwait(false);
        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId: traceId,
            eventType: "ledger.tombstone",
            payloadJson: payloadJson,
            prevHash: prevHash ?? string.Empty);

        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }
}
