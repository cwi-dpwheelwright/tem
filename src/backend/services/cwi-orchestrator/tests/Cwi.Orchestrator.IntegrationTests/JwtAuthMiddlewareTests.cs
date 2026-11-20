using System.Net;
using System.Security.Claims;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Cwi.Orchestrator.IntegrationTests;

public sealed class JwtAuthMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ReturnsUnauthorized_WhenMissingToken()
    {
        var options = new AuthOptions { AuthRequired = true };
        var validator = new StubValidator(JwtValidationResult.Fail(
            new DomainError(ErrorCodes.Unauthorized, "invalid"), 401));
        var middleware = new JwtAuthMiddleware(_ => Task.CompletedTask);
        var context = CreateContextWithEndpoint();

        await middleware.InvokeAsync(context, options, validator);

        Assert.Equal((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_AllowsRequest_WhenAuthNotRequired()
    {
        var options = new AuthOptions { AuthRequired = false };
        var validator = new StubValidator(JwtValidationResult.Fail(
            new DomainError(ErrorCodes.Unauthorized, "invalid"), 401));
        var called = false;
        var middleware = new JwtAuthMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        var context = CreateContextWithEndpoint();

        await middleware.InvokeAsync(context, options, validator);

        Assert.True(called);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsUnauthorized_WhenTokenInvalid()
    {
        var options = new AuthOptions { AuthRequired = true };
        var validator = new StubValidator(JwtValidationResult.Fail(
            new DomainError(ErrorCodes.Unauthorized, "invalid"), 401));
        var middleware = new JwtAuthMiddleware(_ => Task.CompletedTask);
        var context = CreateContextWithEndpoint();
        context.Request.Headers.Authorization = "Bearer invalid";

        await middleware.InvokeAsync(context, options, validator);

        Assert.Equal((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_SetsUser_WhenTokenValid()
    {
        var options = new AuthOptions { AuthRequired = true };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "user-1"),
            new Claim("tenant_id", "tenant-1")
        }, "jwt"));
        var validator = new StubValidator(JwtValidationResult.Success(principal));
        var called = false;
        var middleware = new JwtAuthMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });
        var context = CreateContextWithEndpoint();
        context.Request.Headers.Authorization = "Bearer valid";

        await middleware.InvokeAsync(context, options, validator);

        Assert.True(called);
        Assert.Equal("user-1", context.User.FindFirst("sub")?.Value);
    }

    private static DefaultHttpContext CreateContextWithEndpoint()
    {
        var context = new DefaultHttpContext();
        var metadata = new EndpointMetadataCollection(new JwtRequiredAttribute());
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "jwt"));
        return context;
    }

    private sealed class StubValidator : IJwtValidator
    {
        private readonly JwtValidationResult _result;

        public StubValidator(JwtValidationResult result)
        {
            _result = result;
        }

        public Task<JwtValidationResult> ValidateAsync(string token, CancellationToken cancellationToken)
        {
            return Task.FromResult(_result);
        }
    }
}
