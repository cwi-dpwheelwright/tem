using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.Pipeline;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Cwi.Router.Application.Handlers;

public sealed class AuthHandler : IRouterHandler
{
    private readonly RouterConfig _config;
    private readonly HttpClient _httpClient;
    private static readonly ConcurrentDictionary<string, TokenEpochCacheEntry> TokenEpochCache = new();

    public AuthHandler(RouterConfig config, IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<ServiceResponse<RouterResult>> HandleAsync(
        RouterRequestContext context,
        Func<Task<ServiceResponse<RouterResult>>> next,
        CancellationToken cancellationToken)
    {
        if (!_config.AuthRequired)
        {
            return await next().ConfigureAwait(false);
        }

        var authHeader = context.HttpContext.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.Unauthorized, "Missing bearer token."),
                statusCode: 401,
                message: "Unauthorized");
        }

        if (string.IsNullOrWhiteSpace(_config.JwksUrl))
        {
            return ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "JWKS_URL is not configured."),
                statusCode: 503,
                message: "Auth configuration missing");
        }

        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.Unauthorized, "Empty bearer token."),
                statusCode: 401,
                message: "Unauthorized");
        }

        try
        {
            var jwksJson = await _httpClient.GetStringAsync(_config.JwksUrl, cancellationToken).ConfigureAwait(false);
            var keys = new JsonWebKeySet(jwksJson).GetSigningKeys();

            var tokenHandler = new JwtSecurityTokenHandler();
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = !string.IsNullOrWhiteSpace(_config.JwtIssuer),
                ValidIssuer = _config.JwtIssuer,
                ValidateAudience = !string.IsNullOrWhiteSpace(_config.JwtAudience),
                ValidAudience = _config.JwtAudience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = keys,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            var principal = tokenHandler.ValidateToken(token, parameters, out _);
            var userId = principal.FindFirst("sub")?.Value
                ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var tenantId = principal.FindFirst("tenant_id")?.Value;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                tenantId = _config.DefaultTenantId;
            }
            var tokenVersionRaw = principal.FindFirst("token_version")?.Value;

            context.ClientId = userId;

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(tenantId))
            {
                return ServiceResponse<RouterResult>.Fail(
                    new DomainError(ErrorCodes.Unauthorized, "Missing token claims."),
                    statusCode: 401,
                    message: "Unauthorized");
            }

            if (!int.TryParse(tokenVersionRaw, out var tokenVersion))
            {
                return ServiceResponse<RouterResult>.Fail(
                    new DomainError(ErrorCodes.Unauthorized, "Invalid token version."),
                    statusCode: 401,
                    message: "Unauthorized");
            }

            context.TenantId = tenantId;

            var epochCheck = await ValidateTokenVersionAsync(tenantId, userId, tokenVersion, cancellationToken).ConfigureAwait(false);
            if (!epochCheck.IsSuccess)
            {
                return epochCheck;
            }
        }
        catch (Exception)
        {
            return ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.Unauthorized, "Token validation failed."),
                statusCode: 401,
                message: "Unauthorized");
        }

        return await next().ConfigureAwait(false);
    }

    private async Task<ServiceResponse<RouterResult>> ValidateTokenVersionAsync(
        string tenantId,
        string userId,
        int tokenVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.IdentityUrl))
        {
            return ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "IDENTITY_URL is not configured."),
                statusCode: 503,
                message: "Auth configuration missing");
        }

        var cacheKey = $"{tenantId}:{userId}";
        if (TokenEpochCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
        {
            if (cached.TokenVersion != tokenVersion)
            {
                return ServiceResponse<RouterResult>.Fail(
                    new DomainError(ErrorCodes.Unauthorized, "Token version mismatch."),
                    statusCode: 401,
                    message: "Unauthorized");
            }

            return ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"));
        }

        var baseUrl = _config.IdentityUrl.TrimEnd('/');
        var url = $"{baseUrl}/auth/token-version?tenant_id={Uri.EscapeDataString(tenantId)}&user_id={Uri.EscapeDataString(userId)}";
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, $"Identity token check failed: {ex.GetType().Name}"),
                statusCode: 503,
                message: "Auth configuration missing");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.Unauthorized, "Token revoked."),
                statusCode: 401,
                message: "Unauthorized");
        }

        if (!response.IsSuccessStatusCode)
        {
            return ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Identity token check unavailable."),
                statusCode: 503,
                message: "Auth configuration missing");
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!TryReadTokenVersion(payload, out var currentVersion))
        {
            return ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Invalid token version response."),
                statusCode: 503,
                message: "Auth configuration missing");
        }

        TokenEpochCache[cacheKey] = new TokenEpochCacheEntry(
            currentVersion,
            DateTime.UtcNow.AddSeconds(Math.Max(5, _config.TokenEpochCacheSeconds)));

        if (currentVersion != tokenVersion)
        {
            return ServiceResponse<RouterResult>.Fail(
                new DomainError(ErrorCodes.Unauthorized, "Token version mismatch."),
                statusCode: 401,
                message: "Unauthorized");
        }

        return ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"));
    }

    private static bool TryReadTokenVersion(string json, out int tokenVersion)
    {
        tokenVersion = 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("token_version", out var value))
            {
                return false;
            }
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out tokenVersion))
            {
                return true;
            }
            return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out tokenVersion);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record TokenEpochCacheEntry(int TokenVersion, DateTime ExpiresUtc);
}
