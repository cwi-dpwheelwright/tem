using System.Collections.Concurrent;

namespace Cwi.Orchestrator.Application.BreakGlass;

public sealed class BreakGlassDenylistCache
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revoked = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string tokenId, DateTimeOffset expiresUtc)
    {
        _revoked[tokenId] = expiresUtc;
    }

    public bool IsRevoked(string tokenId)
    {
        if (_revoked.TryGetValue(tokenId, out var expiresUtc))
        {
            if (DateTimeOffset.UtcNow <= expiresUtc)
            {
                return true;
            }

            _revoked.TryRemove(tokenId, out _);
        }

        return false;
    }
}
