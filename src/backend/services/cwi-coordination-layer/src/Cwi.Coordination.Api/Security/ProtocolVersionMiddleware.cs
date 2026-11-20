using System.Text.Json;
using Cwi.Common.Errors;
using Cwi.Coordination.Application;

namespace Cwi.Coordination.Api.Security;

public sealed class ProtocolVersionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ApplicationOptions _options;

    public ProtocolVersionMiddleware(RequestDelegate next, ApplicationOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/membership", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/rounds", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/node-work", StringComparison.OrdinalIgnoreCase))
        {
            if (!context.Request.Headers.TryGetValue("X-Coordination-Protocol", out var version)
                || !string.Equals(version.ToString(), _options.ProtocolVersion, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.Response.ContentType = "application/json";
                var payload = new DomainError(ErrorCodes.InvalidRequest, "Protocol version mismatch.");
                await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
                return;
            }
        }

        await _next(context);
    }
}
