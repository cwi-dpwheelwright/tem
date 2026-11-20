using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Cwi.Common.Errors;
using Microsoft.IdentityModel.Tokens;

namespace Cwi.Orchestrator.Api.Auth;

public sealed class JwtValidator : IJwtValidator
{
    private readonly AuthOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;

    public JwtValidator(AuthOptions options, IHttpClientFactory httpClientFactory)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<JwtValidationResult> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return JwtValidationResult.Fail(
                new DomainError(ErrorCodes.Unauthorized, "Missing bearer token."),
                statusCode: 401);
        }

        if (string.IsNullOrWhiteSpace(_options.JwksUrl))
        {
            if (!_options.AllowInsecureJwt)
            {
                return JwtValidationResult.Fail(
                    new DomainError(ErrorCodes.DependencyUnavailable, "JWKS_URL is not configured."),
                    statusCode: 503);
            }

            return ValidateInsecure(token);
        }

        try
        {
            var jwksJson = await FetchJwksAsync(cancellationToken).ConfigureAwait(false);
            var keys = new JsonWebKeySet(jwksJson).GetSigningKeys();

            var tokenHandler = new JwtSecurityTokenHandler();
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = !string.IsNullOrWhiteSpace(_options.JwtIssuer),
                ValidIssuer = _options.JwtIssuer,
                ValidateAudience = !string.IsNullOrWhiteSpace(_options.JwtAudience),
                ValidAudience = _options.JwtAudience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = keys,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            var principal = tokenHandler.ValidateToken(token, parameters, out _);
            if (!HasRequiredClaims(principal))
            {
                return JwtValidationResult.Fail(
                    new DomainError(ErrorCodes.Unauthorized, "Missing required token claims."),
                    statusCode: 401);
            }

            return JwtValidationResult.Success(principal);
        }
        catch (SecurityTokenException ex)
        {
            return JwtValidationResult.Fail(
                new DomainError(ErrorCodes.Unauthorized, $"Token validation failed: {ex.GetType().Name}"),
                statusCode: 401);
        }
        catch (Exception ex)
        {
            return JwtValidationResult.Fail(
                new DomainError(ErrorCodes.Unauthorized, $"Token validation failed: {ex.GetType().Name}"),
                statusCode: 401);
        }
    }

    private async Task<string> FetchJwksAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        return await client.GetStringAsync(_options.JwksUrl!, cancellationToken).ConfigureAwait(false);
    }

    private JwtValidationResult ValidateInsecure(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        JwtSecurityToken jwt;
        try
        {
            jwt = handler.ReadJwtToken(token);
        }
        catch (Exception ex)
        {
            return JwtValidationResult.Fail(
                new DomainError(ErrorCodes.Unauthorized, $"Token parsing failed: {ex.GetType().Name}"),
                statusCode: 401);
        }

        if (jwt.ValidTo < DateTime.UtcNow)
        {
            return JwtValidationResult.Fail(
                new DomainError(ErrorCodes.Unauthorized, "Token expired."),
                statusCode: 401);
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "jwt"));
        if (!HasRequiredClaims(principal))
        {
            return JwtValidationResult.Fail(
                new DomainError(ErrorCodes.Unauthorized, "Missing required token claims."),
                statusCode: 401);
        }

        return JwtValidationResult.Success(principal);
    }

    private static bool HasRequiredClaims(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var tenantId = principal.FindFirst("tenant_id")?.Value;

        return !string.IsNullOrWhiteSpace(userId)
            && !string.IsNullOrWhiteSpace(tenantId);
    }
}
