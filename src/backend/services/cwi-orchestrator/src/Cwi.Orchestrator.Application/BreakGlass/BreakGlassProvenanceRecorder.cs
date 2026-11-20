#nullable enable
using System.Text.Json;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.BreakGlass;

public sealed class BreakGlassProvenanceRecorder
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;

    public BreakGlassProvenanceRecorder(ILedgerRepository ledgerRepository, LedgerEventFactory ledgerEventFactory)
    {
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
    }

    public async Task RecordIssuanceAsync(BreakGlassToken token, CancellationToken cancellationToken)
    {
        var payload = new
        {
            token_id = token.TokenId,
            tenant_id = token.TenantId,
            user_id = token.UserId,
            role = token.Role,
            scope = token.Scope,
            reason = token.Reason,
            expires_utc = token.ExpiresUtc,
            created_utc = token.CreatedUtc
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        var prevHash = await _ledgerRepository
            .GetLatestHashAsync(token.TokenId, cancellationToken)
            .ConfigureAwait(false);

        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId: token.TokenId,
            eventType: "break_glass.issued",
            payloadJson: payloadJson,
            prevHash: prevHash ?? string.Empty);

        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordRevocationAsync(BreakGlassToken token, CancellationToken cancellationToken)
    {
        var payload = new
        {
            token_id = token.TokenId,
            tenant_id = token.TenantId,
            user_id = token.UserId,
            revoked_utc = token.RevokedUtc,
            reason = token.Reason
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        var prevHash = await _ledgerRepository
            .GetLatestHashAsync(token.TokenId, cancellationToken)
            .ConfigureAwait(false);

        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId: token.TokenId,
            eventType: "break_glass.revoked",
            payloadJson: payloadJson,
            prevHash: prevHash ?? string.Empty);

        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordExpiryAsync(BreakGlassToken token, CancellationToken cancellationToken)
    {
        var payload = new
        {
            token_id = token.TokenId,
            tenant_id = token.TenantId,
            user_id = token.UserId,
            expires_utc = token.ExpiresUtc
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        var prevHash = await _ledgerRepository
            .GetLatestHashAsync(token.TokenId, cancellationToken)
            .ConfigureAwait(false);

        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId: token.TokenId,
            eventType: "break_glass.expired",
            payloadJson: payloadJson,
            prevHash: prevHash ?? string.Empty);

        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }
}
#nullable disable
