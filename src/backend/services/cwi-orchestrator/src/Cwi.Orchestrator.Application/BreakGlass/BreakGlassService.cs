#nullable enable
using Cwi.Common;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.BreakGlass;

public sealed class BreakGlassService : DomainServiceBase
{
    private readonly BreakGlassPolicyValidator _validator;
    private readonly IBreakGlassTokenRepository _tokenRepository;
    private readonly BreakGlassProvenanceRecorder _provenance;
    private readonly BreakGlassDenylistCache _denylist;
    private readonly IBreakGlassNotifier _notifier;

    public BreakGlassService(
        BreakGlassPolicyValidator validator,
        IBreakGlassTokenRepository tokenRepository,
        BreakGlassProvenanceRecorder provenance,
        BreakGlassDenylistCache denylist,
        IBreakGlassNotifier notifier)
    {
        _validator = validator;
        _tokenRepository = tokenRepository;
        _provenance = provenance;
        _denylist = denylist;
        _notifier = notifier;
    }

    public Task<ServiceResponse<BreakGlassIssueResponseDto>> IssueAsync(
        BreakGlassIssueRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            var validation = _validator.Validate(new BreakGlassRequest
            {
                Role = request.Role,
                Scope = request.Scope,
                DurationMinutes = request.DurationMinutes,
                Reason = request.Reason
            });

            if (!validation.IsSuccess)
            {
                return ServiceResponse<BreakGlassIssueResponseDto>.Fail(
                    validation.Error ?? new Cwi.Common.Errors.DomainError(Cwi.Common.Errors.ErrorCodes.InvalidRequest, "Break-glass invalid."),
                    statusCode: validation.StatusCode,
                    message: validation.Message);
            }

            var tokenId = Guid.NewGuid().ToString("N");
            var now = DateTimeOffset.UtcNow;
            var token = new BreakGlassToken
            {
                Id = Guid.NewGuid(),
                TokenId = tokenId,
                TenantId = request.TenantId,
                UserId = request.UserId,
                Role = request.Role,
                Scope = request.Scope,
                Reason = request.Reason ?? string.Empty,
                CreatedUtc = now,
                ExpiresUtc = now.AddMinutes(request.DurationMinutes)
            };

            await _tokenRepository.AddAsync(token, cancellationToken).ConfigureAwait(false);
            await _provenance.RecordIssuanceAsync(token, cancellationToken).ConfigureAwait(false);
            await _notifier.NotifyAsync(new BreakGlassNotification
            {
                EventType = "issued",
                TokenId = token.TokenId,
                TenantId = token.TenantId,
                UserId = token.UserId,
                Role = token.Role,
                Scope = token.Scope,
                OccurredUtc = token.CreatedUtc
            }, cancellationToken).ConfigureAwait(false);

            return ServiceResponse<BreakGlassIssueResponseDto>.Success(new BreakGlassIssueResponseDto
            {
                TokenId = tokenId,
                ExpiresUtc = token.ExpiresUtc
            });
        });
    }

    public Task<ServiceResponse<BreakGlassRevokeResponseDto>> RevokeAsync(
        BreakGlassRevokeRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(request.TokenId))
            {
                return ServiceResponse<BreakGlassRevokeResponseDto>.Fail(
                    new Cwi.Common.Errors.DomainError(Cwi.Common.Errors.ErrorCodes.InvalidRequest, "Token id required."),
                    statusCode: 400,
                    message: "Token id required");
            }

            var token = await _tokenRepository.GetByTokenIdAsync(request.TokenId, cancellationToken).ConfigureAwait(false);
            if (token is null)
            {
                return ServiceResponse<BreakGlassRevokeResponseDto>.Fail(
                    new Cwi.Common.Errors.DomainError(Cwi.Common.Errors.ErrorCodes.InvalidRequest, "Break-glass token not found."),
                    statusCode: 404,
                    message: "Break-glass token not found");
            }

            if (token.RevokedUtc is not null)
            {
                return ServiceResponse<BreakGlassRevokeResponseDto>.Success(new BreakGlassRevokeResponseDto
                {
                    TokenId = token.TokenId,
                    RevokedUtc = token.RevokedUtc.Value
                });
            }

            var now = DateTimeOffset.UtcNow;
            var isExpired = now > token.ExpiresUtc;
            token.RevokedUtc = now;
            await _tokenRepository.RevokeAsync(token, cancellationToken).ConfigureAwait(false);
            _denylist.Add(token.TokenId, token.ExpiresUtc);
            if (isExpired)
            {
                await _provenance.RecordExpiryAsync(token, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _provenance.RecordRevocationAsync(token, cancellationToken).ConfigureAwait(false);
            }
            await _notifier.NotifyAsync(new BreakGlassNotification
            {
                EventType = "revoked",
                TokenId = token.TokenId,
                TenantId = token.TenantId,
                UserId = token.UserId,
                Role = token.Role,
                Scope = token.Scope,
                OccurredUtc = token.RevokedUtc.Value
            }, cancellationToken).ConfigureAwait(false);

            return ServiceResponse<BreakGlassRevokeResponseDto>.Success(new BreakGlassRevokeResponseDto
            {
                TokenId = token.TokenId,
                RevokedUtc = token.RevokedUtc.Value
            });
        });
    }
}
#nullable disable
