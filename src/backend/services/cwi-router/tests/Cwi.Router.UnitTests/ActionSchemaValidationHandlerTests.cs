using Cwi.Common;
using Cwi.Router.Application.Handlers;
using Cwi.Router.Application.Pipeline;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Cwi.Router.UnitTests;

public sealed class ActionSchemaValidationHandlerTests
{
    [Fact]
    public async Task RejectsMissingActionType()
    {
        var handler = new ActionSchemaValidationHandler();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/action";
        var context = new RouterRequestContext(httpContext, "{\"action\":{\"payload\":{\"value\":\"missing\"}}}");

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task AllowsValidActionType()
    {
        var handler = new ActionSchemaValidationHandler();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/action";
        var context = new RouterRequestContext(httpContext, "{\"action\":{\"type\":\"risk_demo\",\"payload\":{}}}");

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SkipsNonActionPath()
    {
        var handler = new ActionSchemaValidationHandler();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/ask";
        var context = new RouterRequestContext(httpContext, "{\"action\":{}}");

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
    }
}
