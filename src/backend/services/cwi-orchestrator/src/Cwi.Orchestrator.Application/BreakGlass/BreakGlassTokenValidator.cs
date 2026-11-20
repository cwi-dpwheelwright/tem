using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.BreakGlass;

public sealed class BreakGlassTokenValidator
{
    private readonly IBreakGlassTokenRepository _tokenRepository;
    private readonly BreakGlassDenylistCache _denylist;

    public BreakGlassTokenValidator(IBreakGlassTokenRepository tokenRepository, BreakGlassDenylistCache denylist)
    {
        _tokenRepository = tokenRepository;
        _denylist = denylist;
    }

    public async Task<ServiceResponse<BreakGlassTokenInfo>> ValidateAsync(
        string tokenId,
        string? requiredScope,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tokenId))
        {
            return ServiceResponse<BreakGlassTokenInfo>.Fail(
                new DomainError(ErrorCodes.Unauthorized, "Break-glass token required."),
                statusCode: 401,
                message: "Break-glass token required");
        }

        if (_denylist.IsRevoked(tokenId))
        {
            return ServiceResponse<BreakGlassTokenInfo>.Fail(
                new DomainError(ErrorCodes.Forbidden, "Break-glass token revoked."),
                statusCode: 403,
                message: "Break-glass token revoked");
        }

        var token = await _tokenRepository.GetByTokenIdAsync(tokenId, cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return ServiceResponse<BreakGlassTokenInfo>.Fail(
                new DomainError(ErrorCodes.Unauthorized, "Break-glass token invalid."),
                statusCode: 401,
                message: "Break-glass token invalid");
        }

        if (token.RevokedUtc is not null)
        {
            return ServiceResponse<BreakGlassTokenInfo>.Fail(
                new DomainError(ErrorCodes.Forbidden, "Break-glass token revoked."),
                statusCode: 403,
                message: "Break-glass token revoked");
        }

        if (DateTimeOffset.UtcNow > token.ExpiresUtc)
        {
            return ServiceResponse<BreakGlassTokenInfo>.Fail(
                new DomainError(ErrorCodes.Forbidden, "Break-glass token expired."),
                statusCode: 403,
                message: "Break-glass token expired");
        }

        if (!string.IsNullOrWhiteSpace(requiredScope) &&
            !string.Equals(token.Scope, requiredScope, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResponse<BreakGlassTokenInfo>.Fail(
                new DomainError(ErrorCodes.Forbidden, "Break-glass scope not permitted."),
                statusCode: 403,
                message: "Break-glass scope not permitted");
        }

        return ServiceResponse<BreakGlassTokenInfo>.Success(new BreakGlassTokenInfo
        {
            TokenId = token.TokenId,
            TenantId = token.TenantId,
            UserId = token.UserId,
            Scope = token.Scope,
            ExpiresUtc = token.ExpiresUtc
        });
    }
}
