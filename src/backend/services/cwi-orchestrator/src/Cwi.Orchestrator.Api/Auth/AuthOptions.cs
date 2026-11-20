namespace Cwi.Orchestrator.Api.Auth;

public sealed class AuthOptions
{
    public bool AuthRequired { get; init; } = true;
    public string? JwksUrl { get; init; }
    public string? JwtIssuer { get; init; }
    public string? JwtAudience { get; init; }
    public bool AllowInsecureJwt { get; init; }
    public string? AdminTenantId { get; init; }

    public static AuthOptions LoadFromEnvironment()
    {
        return new AuthOptions
        {
            AuthRequired = ParseBool("AUTH_REQUIRED", defaultValue: true),
            JwksUrl = Environment.GetEnvironmentVariable("JWKS_URL"),
            JwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER"),
            JwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE"),
            AdminTenantId = Environment.GetEnvironmentVariable("CWI_ADMIN_TENANT_ID")
                ?? Environment.GetEnvironmentVariable("IDENTITY_SUPERADMIN_TENANT_ID")
                ?? Environment.GetEnvironmentVariable("IDENTITY_ADMIN_TENANT_ID"),
            AllowInsecureJwt = ParseBool("ORCHESTRATOR_ALLOW_INSECURE_JWT", defaultValue: false)
                || ParseBool("ALLOW_INSECURE_JWT", defaultValue: false)
        };
    }

    private static bool ParseBool(string name, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return defaultValue;
    }
}
