using System.Collections.Concurrent;

namespace Cwi.Orchestrator.Application.Policy;

public sealed class KillSwitchTokenStore
{
    private readonly ConcurrentDictionary<string, KillSwitchToken> _tokens = new();

    public KillSwitchToken Issue(string tenantId, TimeSpan ttl)
    {
        CleanupExpired();
        var token = new KillSwitchToken
        {
            TokenId = Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            ExpiresUtc = DateTimeOffset.UtcNow.Add(ttl)
        };
        _tokens[token.TokenId] = token;
        return token;
    }

    public bool TryConsume(string tokenId, string tenantId, out string? reason)
    {
        reason = null;
        CleanupExpired();

        if (!_tokens.TryGetValue(tokenId, out var token))
        {
            reason = "token_not_found";
            return false;
        }

        if (!string.Equals(token.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
        {
            reason = "token_tenant_mismatch";
            return false;
        }

        if (token.ExpiresUtc <= DateTimeOffset.UtcNow)
        {
            _tokens.TryRemove(tokenId, out _);
            reason = "token_expired";
            return false;
        }

        if (token.UsedUtc.HasValue)
        {
            reason = "token_replayed";
            return false;
        }

        token.UsedUtc = DateTimeOffset.UtcNow;
        _tokens[tokenId] = token;
        return true;
    }

    private void CleanupExpired()
    {
        foreach (var pair in _tokens)
        {
            if (pair.Value.ExpiresUtc <= DateTimeOffset.UtcNow)
            {
                _tokens.TryRemove(pair.Key, out _);
            }
        }
    }
}

public sealed class KillSwitchToken
{
    public string TokenId { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public DateTimeOffset ExpiresUtc { get; init; }
    public DateTimeOffset? UsedUtc { get; set; }
}
