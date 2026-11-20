using System.Net;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;
using Cwi.Common;
using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.Handlers;
using Cwi.Router.Application.Pipeline;

namespace Cwi.Router.IntegrationTests;

public sealed class RateLimitTests : IAsyncLifetime
{
    private readonly Lazy<IContainer> _redis = new(() => new ContainerBuilder()
        .WithImage("redis:7.2-alpine")
        .WithPortBinding(6379, true)
        .Build());

    public async Task InitializeAsync()
    {
        await _redis.Value.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _redis.Value.DisposeAsync();
    }

    [Fact]
    public async Task RateLimit_ResetsAfterWindow()
    {
        var redisUrl = $"localhost:{_redis.Value.GetMappedPublicPort(6379)}";
        Environment.SetEnvironmentVariable("ROUTER_HTTP_PORT", "8081");
        Environment.SetEnvironmentVariable("ORCHESTRATOR_URL", "http://localhost:8080");
        Environment.SetEnvironmentVariable("AUTH_REQUIRED", "false");
        Environment.SetEnvironmentVariable("MTLS_REQUIRED", "false");
        Environment.SetEnvironmentVariable("REDIS_URL", redisUrl);
        Environment.SetEnvironmentVariable("CWI_RATE_LIMIT", "2");
        Environment.SetEnvironmentVariable("CWI_RATE_WINDOW_SECONDS", "1");
        Environment.SetEnvironmentVariable("MODEL_PRO_URL", "http://localhost:11434");
        Environment.SetEnvironmentVariable("MODEL_FREE_URL", "http://localhost:11435");
        Environment.SetEnvironmentVariable("MODEL_HEALTH_PATH", "/api/tags");
        Environment.SetEnvironmentVariable("MODEL_CB_FAILURE_THRESHOLD", "3");
        Environment.SetEnvironmentVariable("MODEL_CB_COOLDOWN_SECONDS", "30");
        Environment.SetEnvironmentVariable("CWI_POLICY_MODE_DEFAULT", "standard");
        Environment.SetEnvironmentVariable("ORCHESTRATOR_TIMEOUT_SECONDS", "5");

        var config = RouterConfig.LoadFromEnvironment();
        var handler = new RateLimitHandler(config);

        var httpContext = new DefaultHttpContext();
        var context = new RouterRequestContext(httpContext, "{}")
        {
            ClientId = "rate-test-client"
        };

        Task<ServiceResponse<RouterResult>> Next() =>
            Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, string.Empty, "application/json")));

        var first = await handler.HandleAsync(context, Next, default);
        var second = await handler.HandleAsync(context, Next, default);
        var third = await handler.HandleAsync(context, Next, default);

        first.StatusCode.Should().Be((int)HttpStatusCode.OK);
        second.StatusCode.Should().Be((int)HttpStatusCode.OK);
        third.StatusCode.Should().Be((int)HttpStatusCode.TooManyRequests);

        await Task.Delay(1200);

        var afterWindow = await handler.HandleAsync(context, Next, default);
        afterWindow.StatusCode.Should().Be((int)HttpStatusCode.OK);
    }
}
