namespace Cwi.Orchestrator.Api.Auth;

public interface IJwtValidator
{
    Task<JwtValidationResult> ValidateAsync(string token, CancellationToken cancellationToken);
}
