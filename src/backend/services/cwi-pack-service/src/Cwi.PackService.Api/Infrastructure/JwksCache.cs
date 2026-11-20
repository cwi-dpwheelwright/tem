using Microsoft.IdentityModel.Tokens;

namespace Cwi.PackService.Api.Infrastructure;

internal static class JwksCache
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static IReadOnlyList<SecurityKey>? _keys;
    private static DateTimeOffset _expiresUtc;

    public static async Task<IReadOnlyList<SecurityKey>> GetSigningKeysAsync(
        Configuration.AuthConfig config,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken)
    {
        if (_keys is not null && _expiresUtc > DateTimeOffset.UtcNow)
        {
            return _keys;
        }

        await Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_keys is not null && _expiresUtc > DateTimeOffset.UtcNow)
            {
                return _keys;
            }

            using var client = httpClientFactory.CreateClient();
            var jwksJson = await client.GetStringAsync(config.JwksUrl!, cancellationToken).ConfigureAwait(false);
            var keys = new JsonWebKeySet(jwksJson).GetSigningKeys().ToList();
            _keys = keys;
            _expiresUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, config.JwksCacheSeconds));
            return keys;
        }
        finally
        {
            Lock.Release();
        }
    }
}
