using Cwi.Common;
using Cwi.Router.Application.Handlers;
using Cwi.Router.Application.Metrics;
using Cwi.Router.Application.Pipeline;
using Cwi.Router.Application.Survival;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Cwi.Router.UnitTests;

public sealed class SurvivalPriorityHandlerTests
{
    [Fact]
    public async Task BlocksLowPriorityWhenSurvival()
    {
        var store = new SurvivalStateStore();
        store.Update("survival", "test");
        var metrics = new RouterMetrics();
        var handler = new SurvivalPriorityHandler(store, metrics);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/ask";
        var context = new RouterRequestContext(httpContext, "{}");

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task AllowsHighPriorityWhenSurvival()
    {
        var store = new SurvivalStateStore();
        store.Update("survival", "test");
        var metrics = new RouterMetrics();
        var handler = new SurvivalPriorityHandler(store, metrics);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/action";
        httpContext.Request.Headers["x-priority"] = "high";
        var context = new RouterRequestContext(httpContext, "{}");

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task IgnoresNonActionPaths()
    {
        var store = new SurvivalStateStore();
        store.Update("survival", "test");
        var metrics = new RouterMetrics();
        var handler = new SurvivalPriorityHandler(store, metrics);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/health";
        var context = new RouterRequestContext(httpContext, string.Empty);

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
    }
}
