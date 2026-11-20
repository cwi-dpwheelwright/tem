namespace Cwi.Coordination.Api.Security;

public sealed class AdminAuthorizationOptions
{
    public bool RequireToken { get; init; } = false;
    public string HeaderName { get; init; } = "X-Admin-Token";
    public string? Token { get; init; }
}
