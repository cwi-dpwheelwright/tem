using Cwi.Common;
using Cwi.Router.Application.Handlers;
using Cwi.Router.Application.Pipeline;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Cwi.Router.UnitTests;

public sealed class TenantRoutingHandlerTests
{
    [Fact]
    public async Task UsesTenantFromContextWhenPresent()
    {
        var handler = new TenantRoutingHandler("tenant-default");
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/reports";
        httpContext.Request.Headers["x-tenant-id"] = "tenant-01";
        var context = new RouterRequestContext(httpContext, string.Empty)
        {
            TenantId = "tenant-01"
        };

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.TenantId.Should().Be("tenant-01");
    }

    [Fact]
    public async Task RejectsMismatchedClientTenantOverride()
    {
        var handler = new TenantRoutingHandler("tenant-default");
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/reports";
        httpContext.Request.Headers["x-tenant-id"] = "tenant-02";
        var context = new RouterRequestContext(httpContext, string.Empty)
        {
            TenantId = "tenant-01"
        };

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task UsesDefaultTenantWhenMissing()
    {
        var handler = new TenantRoutingHandler("tenant-01");
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/reports";
        var context = new RouterRequestContext(httpContext, string.Empty);

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.TenantId.Should().Be("tenant-01");
    }

    [Fact]
    public async Task RejectsMissingTenantIdForProtectedPath()
    {
        var handler = new TenantRoutingHandler();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/reports";
        var context = new RouterRequestContext(httpContext, string.Empty);

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task UsesClientTenantIdWhenContextAndDefaultMissing()
    {
        var handler = new TenantRoutingHandler();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/ask";
        var context = new RouterRequestContext(httpContext, "{\"tenant_id\":\"tenant-42\"}");

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.TenantId.Should().Be("tenant-42");
    }

    [Fact]
    public async Task AllowsMissingTenantIdForExemptPath()
    {
        var handler = new TenantRoutingHandler();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/trace/trace-123";
        var context = new RouterRequestContext(httpContext, string.Empty);

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
    }
}
