namespace Cwi.PackService.Api.Configuration;

public sealed class AuthConfig
{
    public bool AuthRequired { get; init; }
    public string? JwksUrl { get; init; }
    public string? JwtIssuer { get; init; }
    public string? JwtAudience { get; init; }
    public string AdminRole { get; init; } = "admin";
    public int JwksCacheSeconds { get; init; }

    public static AuthConfig FromEnvironment()
    {
        var authRequired = ParseBool(Environment.GetEnvironmentVariable("AUTH_REQUIRED") ?? "true");
        var jwksUrl = Environment.GetEnvironmentVariable("JWKS_URL");
        var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER");
        var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE");
        var adminRole = Environment.GetEnvironmentVariable("PACK_ADMIN_ROLE") ?? "admin";
        var cacheSeconds = GetIntFromEnv("JWKS_CACHE_SECONDS", 300);

        return new AuthConfig
        {
            AuthRequired = authRequired,
            JwksUrl = jwksUrl,
            JwtIssuer = jwtIssuer,
            JwtAudience = jwtAudience,
            AdminRole = adminRole,
            JwksCacheSeconds = cacheSeconds
        };
    }

    private static bool ParseBool(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && bool.TryParse(value, out var parsed) && parsed;
    }

    private static int GetIntFromEnv(string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var value) ? value : fallback;
    }
}
