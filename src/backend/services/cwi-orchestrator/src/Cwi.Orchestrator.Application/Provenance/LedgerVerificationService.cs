using Cwi.Orchestrator.Domain;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Provenance;

public sealed class LedgerVerificationService
{
    private readonly ILedgerRepository _ledgerRepository;
    private readonly ISignatureProvider _signatureProvider;

    public LedgerVerificationService(ILedgerRepository ledgerRepository, ISignatureProvider signatureProvider)
    {
        _ledgerRepository = ledgerRepository;
        _signatureProvider = signatureProvider;
    }

    public async Task<LedgerVerificationResult> VerifyAsync(string traceId, CancellationToken cancellationToken)
    {
        var events = await _ledgerRepository
            .GetByTraceIdAsync(traceId, skip: 0, take: int.MaxValue, cancellationToken)
            .ConfigureAwait(false);

        if (events.Count == 0)
        {
            return new LedgerVerificationResult
            {
                IsValid = true
            };
        }

        string? prevHash = null;
        foreach (var ledgerEvent in events.OrderBy(e => e.CreatedUtc))
        {
            if (!string.Equals(ledgerEvent.PrevHash, prevHash ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return new LedgerVerificationResult
                {
                    IsValid = false,
                    Error = $"PrevHash mismatch at event {ledgerEvent.Id}"
                };
            }

            var computed = LedgerHasher.ComputeHash(
                prevHash ?? string.Empty,
                ledgerEvent.TraceId,
                ledgerEvent.EventType,
                ledgerEvent.PayloadJson,
                ledgerEvent.CreatedUtc,
                ledgerEvent.HashAlgorithm);

            if (!string.Equals(computed, ledgerEvent.Hash, StringComparison.OrdinalIgnoreCase))
            {
                return new LedgerVerificationResult
                {
                    IsValid = false,
                    Error = $"Hash mismatch at event {ledgerEvent.Id}"
                };
            }

            if (!string.IsNullOrWhiteSpace(ledgerEvent.Signature) &&
                !string.IsNullOrWhiteSpace(ledgerEvent.SignatureAlgorithm) &&
                !string.IsNullOrWhiteSpace(ledgerEvent.KeyId))
            {
                var signature = new SignatureResult(
                    ledgerEvent.Signature,
                    ledgerEvent.SignatureAlgorithm,
                    ledgerEvent.KeyId);

                if (!_signatureProvider.Verify(ledgerEvent.Hash, signature))
                {
                    return new LedgerVerificationResult
                    {
                        IsValid = false,
                        Error = $"Signature invalid at event {ledgerEvent.Id}"
                    };
                }
            }

            prevHash = ledgerEvent.Hash;
        }

        return new LedgerVerificationResult
        {
            IsValid = true
        };
    }
}
