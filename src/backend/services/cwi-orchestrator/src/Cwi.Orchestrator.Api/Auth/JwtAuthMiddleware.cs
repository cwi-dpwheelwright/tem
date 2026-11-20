using System.Security.Claims;
using Cwi.Common.Errors;
using Microsoft.AspNetCore.Authorization;

namespace Cwi.Orchestrator.Api.Auth;

public sealed class JwtAuthMiddleware
{
    private readonly RequestDelegate _next;

    public JwtAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, AuthOptions options, IJwtValidator validator)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var required = endpoint?.Metadata.GetMetadata<JwtRequiredAttribute>();
        if (required is null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!options.AuthRequired)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await WriteUnauthorizedAsync(context, new DomainError(ErrorCodes.Unauthorized, "Missing bearer token."))
                .ConfigureAwait(false);
            return;
        }

        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            await WriteUnauthorizedAsync(context, new DomainError(ErrorCodes.Unauthorized, "Missing bearer token."))
                .ConfigureAwait(false);
            return;
        }

        var validation = await validator.ValidateAsync(token, context.RequestAborted).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            var error = validation.Error ?? new DomainError(ErrorCodes.Unauthorized, "Unauthorized");
            await WriteFailureAsync(context, validation.StatusCode, error).ConfigureAwait(false);
            return;
        }

        if (validation.Principal is not null)
        {
            context.User = validation.Principal;
        }

        await _next(context).ConfigureAwait(false);
    }

    private static Task WriteUnauthorizedAsync(HttpContext context, DomainError error)
    {
        return WriteFailureAsync(context, 401, error);
    }

    private static Task WriteFailureAsync(HttpContext context, int statusCode, DomainError error)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(error, context.RequestAborted);
    }
}
