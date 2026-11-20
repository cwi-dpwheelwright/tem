#nullable enable
using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Application.Provenance;

public sealed class LedgerEventFactory
{
    private readonly ISignatureProvider _signatureProvider;
    private readonly string _hashSuiteId;
    private readonly LedgerRedactionPolicy? _redactionPolicy;

    public LedgerEventFactory(
        ISignatureProvider signatureProvider,
        string? hashSuiteId = null,
        LedgerRedactionPolicy? redactionPolicy = null)
    {
        _signatureProvider = signatureProvider;
        _hashSuiteId = hashSuiteId ?? Cwi.Orchestrator.Domain.LedgerHasher.DefaultSuite;
        _redactionPolicy = redactionPolicy;
    }

    public LedgerEvent CreateSignedEvent(
        string traceId,
        string eventType,
        string payloadJson,
        string prevHash)
    {
        var redactedPayload = _redactionPolicy?.RedactPayload(payloadJson) ?? payloadJson;
        var ledgerEvent = LedgerEvent.Create(traceId, eventType, redactedPayload, prevHash, _hashSuiteId);
        var signature = _signatureProvider.Sign(ledgerEvent.Hash);
        if (signature is null)
        {
            return ledgerEvent;
        }

        ledgerEvent.Signature = signature.Signature;
        ledgerEvent.SignatureAlgorithm = signature.Algorithm;
        ledgerEvent.KeyId = signature.KeyId;
        return ledgerEvent;
    }
}
#nullable disable
