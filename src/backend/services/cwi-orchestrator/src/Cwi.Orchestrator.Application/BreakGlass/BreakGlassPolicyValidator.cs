using Cwi.Common;
using Cwi.Common.Errors;

namespace Cwi.Orchestrator.Application.BreakGlass;

public sealed class BreakGlassPolicyValidator
{
    private readonly BreakGlassPolicy _policy;

    public BreakGlassPolicyValidator(BreakGlassPolicy policy)
    {
        _policy = policy;
    }

    public ServiceResponse<bool> Validate(BreakGlassRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Role) || !_policy.AllowedRoles.Contains(request.Role, StringComparer.OrdinalIgnoreCase))
        {
            return ServiceResponse<bool>.Fail(
                new DomainError(ErrorCodes.Forbidden, "Role not permitted for break-glass."),
                statusCode: 403,
                message: "Break-glass role not permitted");
        }

        if (string.IsNullOrWhiteSpace(request.Scope) || !_policy.AllowedScopes.Contains(request.Scope, StringComparer.OrdinalIgnoreCase))
        {
            return ServiceResponse<bool>.Fail(
                new DomainError(ErrorCodes.Forbidden, "Scope not permitted for break-glass."),
                statusCode: 403,
                message: "Break-glass scope not permitted");
        }

        if (request.DurationMinutes <= 0 || request.DurationMinutes > _policy.MaxDurationMinutes)
        {
            return ServiceResponse<bool>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Break-glass duration invalid."),
                statusCode: 400,
                message: "Break-glass duration invalid");
        }

        if (_policy.RequireReason && string.IsNullOrWhiteSpace(request.Reason))
        {
            return ServiceResponse<bool>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Break-glass reason required."),
                statusCode: 400,
                message: "Break-glass reason required");
        }

        return ServiceResponse<bool>.Success(true);
    }
}
