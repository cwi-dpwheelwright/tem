using System.Security.Claims;
using Cwi.Common.Errors;

namespace Cwi.Orchestrator.Api.Auth;

public sealed record JwtValidationResult(
    bool IsValid,
    ClaimsPrincipal? Principal,
    DomainError? Error,
    int StatusCode)
{
    public static JwtValidationResult Success(ClaimsPrincipal principal)
    {
        return new JwtValidationResult(true, principal, null, 200);
    }

    public static JwtValidationResult Fail(DomainError error, int statusCode)
    {
        return new JwtValidationResult(false, null, error, statusCode);
    }
}
