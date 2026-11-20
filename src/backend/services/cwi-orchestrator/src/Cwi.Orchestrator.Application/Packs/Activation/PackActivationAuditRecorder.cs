#nullable enable
using System.Text.Json;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Packs.Activation;

public sealed class PackActivationAuditRecorder
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;

    public PackActivationAuditRecorder(ILedgerRepository ledgerRepository, LedgerEventFactory ledgerEventFactory)
    {
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
    }

    public Task RecordActivationAsync(PackActivationState state, CancellationToken cancellationToken)
    {
        return RecordAsync("pack.activation", state, cancellationToken);
    }

    public Task RecordRollbackAsync(PackActivationState state, CancellationToken cancellationToken)
    {
        return RecordAsync("pack.rollback", state, cancellationToken);
    }

    private async Task RecordAsync(
        string eventType,
        PackActivationState state,
        CancellationToken cancellationToken)
    {
        var payload = new PackActivationAuditPayload
        {
            TenantId = state.TenantId,
            PackName = state.PackName,
            PackHash = state.PackHash,
            Status = state.Status,
            ActivatedBy = state.ActivatedBy,
            RolledBackBy = state.RolledBackBy,
            RollbackReason = state.RollbackReason,
            ActivatedUtc = state.ActivatedUtc,
            RolledBackUtc = state.RolledBackUtc,
            UpdatedUtc = state.UpdatedUtc
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(payloadJson);
        var canonicalPayload = CanonicalJson.Canonicalize(document.RootElement);
        var traceId = $"pack-activation:{state.TenantId}:{state.PackName}";
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
