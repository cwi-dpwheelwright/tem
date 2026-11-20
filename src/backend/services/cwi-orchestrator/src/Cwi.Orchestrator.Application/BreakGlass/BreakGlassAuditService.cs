#nullable enable
using System.Linq;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.BreakGlass;

public sealed class BreakGlassAuditService
{
    private readonly IBreakGlassTokenRepository _repository;

    public BreakGlassAuditService(IBreakGlassTokenRepository repository)
    {
        _repository = repository;
    }

    public async Task<ServiceResponse<BreakGlassAuditPage>> QueryAsync(
        string tenantId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ServiceResponse<BreakGlassAuditPage>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Tenant id is required."),
                statusCode: 400,
                message: "Tenant id required");
        }

        var safePage = page < 1 ? 1 : page;
        var safeSize = pageSize switch
        {
            < 1 => 50,
            > 500 => 500,
            _ => pageSize
        };

        var total = await _repository.GetCountAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var skip = (safePage - 1) * safeSize;
        var tokens = await _repository
            .GetByTenantAsync(tenantId, skip, safeSize, cancellationToken)
            .ConfigureAwait(false);

        return ServiceResponse<BreakGlassAuditPage>.Success(new BreakGlassAuditPage
        {
            Page = safePage,
            PageSize = safeSize,
            Total = total,
            Items = tokens.Select(token => new BreakGlassAuditTokenDto
            {
                TokenId = token.TokenId,
                TenantId = token.TenantId,
                UserId = token.UserId,
                Role = token.Role,
                Scope = token.Scope,
                Reason = token.Reason,
                ExpiresUtc = token.ExpiresUtc,
                CreatedUtc = token.CreatedUtc,
                RevokedUtc = token.RevokedUtc
            }).ToList()
        });
    }
}
#nullable disable
