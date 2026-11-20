using System.Security.Cryptography;
using System.Text;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.Application.Services;

public sealed class LedgerVerificationService
{
    private readonly ILedgerRepository _repository;

    public LedgerVerificationService(ILedgerRepository repository)
    {
        _repository = repository;
    }

    public async Task<LedgerVerificationResponse> VerifyAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var events = await _repository.ListAsync(limit, cancellationToken).ConfigureAwait(false);
        var ordered = events
            .OrderBy(e => e.CreatedUtc)
            .ToList();

        string prevHash = string.Empty;
        Guid? lastValidId = null;

        foreach (var ledgerEvent in ordered)
        {
            if (!string.Equals(ledgerEvent.PrevHash ?? string.Empty, prevHash, StringComparison.OrdinalIgnoreCase))
            {
                return new LedgerVerificationResponse
                {
                    Valid = false,
                    CheckedEvents = ordered.Count,
                    LastValidEventId = lastValidId,
                    FailureEventId = ledgerEvent.Id,
                    FailureReason = "prev_hash mismatch",
                    CheckedUtc = DateTimeOffset.UtcNow
                };
            }

            var expectedHash = ComputeHash(prevHash, ledgerEvent.EventType, ledgerEvent.PayloadJson);
            if (!string.Equals(ledgerEvent.Hash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                return new LedgerVerificationResponse
                {
                    Valid = false,
                    CheckedEvents = ordered.Count,
                    LastValidEventId = lastValidId,
                    FailureEventId = ledgerEvent.Id,
                    FailureReason = "hash mismatch",
                    CheckedUtc = DateTimeOffset.UtcNow
                };
            }

            prevHash = ledgerEvent.Hash;
            lastValidId = ledgerEvent.Id;
        }

        return new LedgerVerificationResponse
        {
            Valid = true,
            CheckedEvents = ordered.Count,
            LastValidEventId = lastValidId,
            CheckedUtc = DateTimeOffset.UtcNow
        };
    }

    private static string ComputeHash(string prevHash, string eventType, string payloadJson)
    {
        var data = $"{prevHash}:{eventType}:{payloadJson}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
