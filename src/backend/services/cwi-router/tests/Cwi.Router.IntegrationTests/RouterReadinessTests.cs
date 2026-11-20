using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Cwi.Router.Api.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace Cwi.Router.IntegrationTests;

public sealed class RouterReadinessTests
{
    [Fact]
    public async Task Ready_ReturnsServiceUnavailable_WhenRedisMissing()
    {
        RouterReadinessTestHelpers.ConfigureEnvironment(redisUrl: string.Empty);
        using var factory = RouterReadinessTestHelpers.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var payload = await response.Content.ReadFromJsonAsync<ReadyResponse>();
        payload.Should().NotBeNull();
        payload!.Ready.Should().BeFalse();
        payload.Checks.Should().ContainKey("redis");
        payload.Checks["redis"].Status.Should().Be("down");
    }

    [Fact]
    public async Task Ready_ReturnsServiceUnavailable_WhenOrchestratorMissing()
    {
        RouterReadinessTestHelpers.ConfigureEnvironment(redisUrl: string.Empty);
        using var factory = RouterReadinessTestHelpers.CreateFactory(
            new RouterReadinessTestHelpers.FailingHttpMessageHandler());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var payload = await response.Content.ReadFromJsonAsync<ReadyResponse>();
        payload.Should().NotBeNull();
        payload!.Ready.Should().BeFalse();
        payload.Checks.Should().ContainKey("orchestrator");
        payload.Checks["orchestrator"].Status.Should().Be("down");
        payload.Checks["orchestrator"].Detail.Should().Be(nameof(HttpRequestException));
    }
}

public sealed class RouterReadinessDockerTests : IAsyncLifetime
{
    private readonly Lazy<IContainer> _redis = new(() => new ContainerBuilder()
        .WithImage("redis:7.2-alpine")
        .WithPortBinding(6379, true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilPortIsAvailable(6379)
            .UntilMessageIsLogged("Ready to accept connections"))
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
    public async Task Ready_ReturnsOk_WhenDependenciesHealthy()
    {
        var redisUrl = $"localhost:{_redis.Value.GetMappedPublicPort(6379)}";
        RouterReadinessTestHelpers.ConfigureEnvironment(redisUrl);
        await RouterReadinessTestHelpers.WaitForRedisAsync(redisUrl, TimeSpan.FromSeconds(10));
        using var factory = RouterReadinessTestHelpers.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/ready");

        var payload = await response.Content.ReadFromJsonAsync<ReadyResponse>();
        payload.Should().NotBeNull();
        var details = payload is null
            ? "payload=null"
            : string.Join(
                ", ",
                payload.Checks.Select(entry => $"{entry.Key}:{entry.Value.Status}:{entry.Value.Detail}"));
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"ready checks: {details}");
        payload!.Ready.Should().BeTrue();
        payload.Checks.Should().ContainKey("orchestrator");
        payload.Checks.Should().ContainKey("redis");
    }

}

internal static class RouterReadinessTestHelpers
{
    public static WebApplicationFactory<Program> CreateFactory(HttpMessageHandler? handler = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddHttpClient("orchestrator")
                        .ConfigurePrimaryHttpMessageHandler(() => handler ?? new OkHttpMessageHandler());
                });
            });
    }

    public static void ConfigureEnvironment(string? redisUrl)
    {
        Environment.SetEnvironmentVariable("ROUTER_HTTP_PORT", "8081");
        Environment.SetEnvironmentVariable("ORCHESTRATOR_URL", "http://localhost:8080");
        Environment.SetEnvironmentVariable("AUTH_REQUIRED", "false");
        Environment.SetEnvironmentVariable("MTLS_REQUIRED", "false");
        Environment.SetEnvironmentVariable("REDIS_URL", redisUrl);
        Environment.SetEnvironmentVariable("CWI_RATE_LIMIT", "0");
        Environment.SetEnvironmentVariable("CWI_RATE_WINDOW_SECONDS", "60");
        Environment.SetEnvironmentVariable("MODEL_PRO_URL", "http://localhost:11434");
        Environment.SetEnvironmentVariable("MODEL_FREE_URL", "http://localhost:11435");
        Environment.SetEnvironmentVariable("MODEL_HEALTH_PATH", "/api/tags");
        Environment.SetEnvironmentVariable("MODEL_CB_FAILURE_THRESHOLD", "3");
        Environment.SetEnvironmentVariable("MODEL_CB_COOLDOWN_SECONDS", "30");
        Environment.SetEnvironmentVariable("CWI_POLICY_MODE_DEFAULT", "standard");
        Environment.SetEnvironmentVariable("ORCHESTRATOR_TIMEOUT_SECONDS", "5");
    }

    internal sealed class OkHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty)
            };
            return Task.FromResult(response);
        }
    }

    internal sealed class FailingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Simulated orchestrator failure.");
        }
    }

    internal static async Task WaitForRedisAsync(string redisUrl, TimeSpan timeout)
    {
        var options = ConfigurationOptions.Parse(redisUrl);
        options.AbortOnConnectFail = false;
        var deadline = DateTimeOffset.UtcNow + timeout;
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var connection = await ConnectionMultiplexer.ConnectAsync(options);
                var db = connection.GetDatabase();
                await db.PingAsync();
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        throw new TimeoutException($"Redis did not respond to PING. Last error: {lastError?.GetType().Name}");
    }
}
