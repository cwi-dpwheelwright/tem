using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.PackService.Api.Configuration;
using Cwi.PackService.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace Cwi.PackService.Api.Infrastructure;

public sealed class PackAuthService
{
    private readonly AuthConfig _config;
    private readonly IHttpClientFactory _httpClientFactory;

    public PackAuthService(AuthConfig config, IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ServiceResponse<PackAuthResult>> EnsureAdminAsync(
        HttpContext context,
        bool requireBreakGlass,
        CancellationToken cancellationToken)
    {
        var hasBreakGlass = HasBreakGlass(context);

        if (!_config.AuthRequired)
        {
            if (requireBreakGlass && !hasBreakGlass)
            {
                return Fail("break_glass_required", "Break-glass token required.", StatusCodes.Status403Forbidden);
            }

            return ServiceResponse<PackAuthResult>.Success(new PackAuthResult(null, null, hasBreakGlass));
        }

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("unauthorized", "Missing bearer token.", StatusCodes.Status401Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(_config.JwksUrl))
        {
            return Fail("auth_unavailable", "JWKS_URL is not configured.", StatusCodes.Status503ServiceUnavailable);
        }

        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Fail("unauthorized", "Missing bearer token.", StatusCodes.Status401Unauthorized);
        }

        ClaimsPrincipal principal;
        try
        {
            var keys = await JwksCache.GetSigningKeysAsync(_config, _httpClientFactory, cancellationToken)
                .ConfigureAwait(false);

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

            principal = tokenHandler.ValidateToken(token, parameters, out _);
        }
        catch (Exception)
        {
            return Fail("unauthorized", "Token validation failed.", StatusCodes.Status401Unauthorized);
        }

        if (!HasAdminRole(principal, _config.AdminRole))
        {
            return Fail("forbidden", "Admin role required.", StatusCodes.Status403Forbidden);
        }

        var actorId = principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        var tenantId = principal.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            tenantId = context.Request.Headers["X-Tenant-Id"].ToString();
        }

        if (requireBreakGlass && !hasBreakGlass)
        {
            return Fail("break_glass_required", "Break-glass token required.", StatusCodes.Status403Forbidden);
        }

        return ServiceResponse<PackAuthResult>.Success(new PackAuthResult(actorId, tenantId, hasBreakGlass));
    }

    private static bool HasBreakGlass(HttpContext context)
    {
        return !string.IsNullOrWhiteSpace(context.Request.Headers["X-Break-Glass-Token"].ToString());
    }

    private static bool HasAdminRole(ClaimsPrincipal principal, string adminRole)
    {
        if (string.IsNullOrWhiteSpace(adminRole))
        {
            return true;
        }

        foreach (var claim in principal.Claims)
        {
            if (claim.Type is "role" or "roles" or "groups" or "scope" or "scp" || claim.Type == ClaimTypes.Role)
            {
                var parts = claim.Value.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Any(part => string.Equals(part, adminRole, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static ServiceResponse<PackAuthResult> Fail(string code, string message, int statusCode)
    {
        return ServiceResponse<PackAuthResult>.Fail(new DomainError(code, message), statusCode, message);
    }
}
