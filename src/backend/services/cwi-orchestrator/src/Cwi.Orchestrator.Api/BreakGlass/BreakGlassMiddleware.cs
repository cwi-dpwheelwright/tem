#nullable enable
using Cwi.Orchestrator.Application.BreakGlass;

namespace Cwi.Orchestrator.Api.BreakGlass;

public sealed class BreakGlassMiddleware
{
    private const string HeaderName = "X-Break-Glass-Token";
    private readonly RequestDelegate _next;

    public BreakGlassMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, BreakGlassTokenValidator validator)
    {
        var endpoint = context.GetEndpoint();
        var scopeAttribute = endpoint?.Metadata.GetMetadata<BreakGlassScopeAttribute>();
        if (scopeAttribute is null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var tokenId = context.Request.Headers[HeaderName].FirstOrDefault();
        var validation = await validator.ValidateAsync(tokenId ?? string.Empty, scopeAttribute.Scope, context.RequestAborted)
            .ConfigureAwait(false);

        if (!validation.IsSuccess)
        {
            context.Response.StatusCode = validation.StatusCode;
            object errorPayload = (object?)validation.Error ?? new { error = validation.Message };
            await context.Response.WriteAsJsonAsync(errorPayload, context.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        context.Items["break_glass_token"] = validation.Data;
        await _next(context).ConfigureAwait(false);
    }
}
#nullable disable
