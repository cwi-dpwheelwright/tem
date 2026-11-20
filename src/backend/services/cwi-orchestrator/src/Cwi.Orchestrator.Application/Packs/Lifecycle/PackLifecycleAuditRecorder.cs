#nullable enable
using System.Text.Json;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Packs.Lifecycle;

public sealed class PackLifecycleAuditRecorder
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;

    public PackLifecycleAuditRecorder(ILedgerRepository ledgerRepository, LedgerEventFactory ledgerEventFactory)
    {
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
    }

    public Task RecordRetireAsync(PackLifecycleAuditPayload payload, CancellationToken cancellationToken)
    {
        return RecordAsync("pack.retire", payload, cancellationToken);
    }

    public Task RecordDeleteAsync(PackLifecycleAuditPayload payload, CancellationToken cancellationToken)
    {
        return RecordAsync("pack.delete", payload, cancellationToken);
    }

    private async Task RecordAsync(
        string eventType,
        PackLifecycleAuditPayload payload,
        CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(payloadJson);
        var canonicalPayload = CanonicalJson.Canonicalize(document.RootElement);
        var traceId = string.IsNullOrWhiteSpace(payload.PackVersion)
            ? $"pack-lifecycle:{payload.PackId}"
            : $"pack-lifecycle:{payload.PackId}:{payload.PackVersion}";
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
