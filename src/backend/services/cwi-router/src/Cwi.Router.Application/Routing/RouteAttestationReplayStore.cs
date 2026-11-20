using System.Collections.Concurrent;
using System.Threading;

namespace Cwi.Router.Application.Routing;

public sealed class RouteAttestationReplayStore
{
    private readonly ConcurrentDictionary<string, long> _sequences = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _nonces = new();
    private readonly TimeSpan _nonceTtl = TimeSpan.FromMinutes(10);
    private int _cleanupCounter;

    public long NextSequence(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            key = "unknown";
        }

        return _sequences.AddOrUpdate(key, 1, (_, current) => current + 1);
    }

    public bool TryRegister(string key, string nonce, long sequence, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = "unknown";
        }

        if (sequence <= 0)
        {
            reason = "sequence_invalid";
            return false;
        }

        if (string.IsNullOrWhiteSpace(nonce))
        {
            reason = "nonce_missing";
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        CleanupNonces(now);

        var last = _sequences.GetOrAdd(key, 0);
        if (sequence <= last)
        {
            reason = "sequence_replay";
            return false;
        }

        var nonceKey = $"{key}:{nonce}";
        if (!_nonces.TryAdd(nonceKey, now))
        {
            reason = "nonce_replay";
            return false;
        }

        _sequences[key] = sequence;
        return true;
    }

    private void CleanupNonces(DateTimeOffset now)
    {
        if (_nonces.Count < 1000 && (Interlocked.Increment(ref _cleanupCounter) % 64 != 0))
        {
            return;
        }

        foreach (var entry in _nonces)
        {
            if (now - entry.Value > _nonceTtl)
            {
                _nonces.TryRemove(entry.Key, out _);
            }
        }
    }
}
