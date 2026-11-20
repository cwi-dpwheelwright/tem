using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.Application.Services;

public sealed class LedgerService
{
    private readonly ILedgerRepository _repository;

    public LedgerService(ILedgerRepository repository)
    {
        _repository = repository;
    }

    public async Task AppendAsync(string eventType, object payload, CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(payload);
        var previous = await _repository.ListAsync(1, cancellationToken).ConfigureAwait(false);
        var prevHash = previous.FirstOrDefault()?.Hash ?? string.Empty;
        var hash = ComputeHash(prevHash, eventType, payloadJson);

        var ledgerEvent = new LedgerEvent
        {
            EventType = eventType,
            PayloadJson = payloadJson,
            PrevHash = prevHash,
            Hash = hash
        };
        await _repository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }

    private static string ComputeHash(string prevHash, string eventType, string payloadJson)
    {
        var data = $"{prevHash}:{eventType}:{payloadJson}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
