using Microsoft.AspNetCore.Http;

namespace Cwi.Coordination.Api.Security;

public sealed class AdminAuthorizationService
{
    private readonly AdminAuthorizationOptions _options;

    public AdminAuthorizationService(AdminAuthorizationOptions options)
    {
        _options = options;
    }

    public bool TryAuthorize(HttpContext context, out string? error)
    {
        error = null;

        if (!_options.RequireToken)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            error = "admin token not configured";
            return false;
        }

        var headerValue = context.Request.Headers[_options.HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            error = "missing admin token";
            return false;
        }

        if (!string.Equals(headerValue.Trim(), _options.Token, StringComparison.Ordinal))
        {
            error = "invalid admin token";
            return false;
        }

        return true;
    }
}
