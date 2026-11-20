using Cwi.Common;
using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.Handlers;
using Cwi.Router.Application.Pipeline;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Cwi.Router.UnitTests;

[Collection("Env")]
public sealed class PolicyModeHandlerTests
{
    [Fact]
    public async Task UsesDefaultWhenHeaderMissing()
    {
        Environment.SetEnvironmentVariable("ORCHESTRATOR_URL", "http://localhost:8081/");
        Environment.SetEnvironmentVariable("CWI_POLICY_MODE_DEFAULT", "standard");

        var config = RouterConfig.LoadFromEnvironment();
        var handler = new PolicyModeHandler(config);
        var context = new RouterRequestContext(new DefaultHttpContext(), body: string.Empty);

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.PolicyMode.Should().Be("standard");
    }

    [Fact]
    public async Task UsesFallbackWhenDefaultMissing()
    {
        Environment.SetEnvironmentVariable("ORCHESTRATOR_URL", "http://localhost:8081/");
        Environment.SetEnvironmentVariable("CWI_POLICY_MODE_DEFAULT", null);

        var config = RouterConfig.LoadFromEnvironment();
        var handler = new PolicyModeHandler(config);
        var context = new RouterRequestContext(new DefaultHttpContext(), body: string.Empty);

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.PolicyMode.Should().Be("standard");
    }
}

