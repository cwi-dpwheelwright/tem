#nullable enable
using System.Text.Json;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.BreakGlass;

public sealed class BreakGlassNotificationRecorder : IBreakGlassNotifier
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly Cwi.Orchestrator.Application.Provenance.LedgerEventFactory _ledgerEventFactory;

    public BreakGlassNotificationRecorder(
        ILedgerRepository ledgerRepository,
        Cwi.Orchestrator.Application.Provenance.LedgerEventFactory ledgerEventFactory)
    {
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
    }

    public async Task NotifyAsync(BreakGlassNotification notification, CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(notification);
        var prevHash = await _ledgerRepository
            .GetLatestHashAsync(notification.TokenId, cancellationToken)
            .ConfigureAwait(false);

        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId: notification.TokenId,
            eventType: $"break_glass.{notification.EventType}",
            payloadJson: payloadJson,
            prevHash: prevHash ?? string.Empty);

        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }
}
#nullable disable
