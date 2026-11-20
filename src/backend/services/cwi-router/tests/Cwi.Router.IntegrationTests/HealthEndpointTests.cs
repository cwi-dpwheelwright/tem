using System.Net;
using System.Net.Http.Json;
using Cwi.Router.Api.Models;
using FluentAssertions;
using Xunit;

namespace Cwi.Router.IntegrationTests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Health_ReturnsOkFalse_WhenRedisMissing()
    {
        RouterReadinessTestHelpers.ConfigureEnvironment(redisUrl: string.Empty);
        using var factory = RouterReadinessTestHelpers.CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        payload.Should().NotBeNull();
        payload!.Ok.Should().BeFalse();
        payload.Services.Should().ContainKey("redis");
        payload.Services["redis"].Should().Contain("REDIS_URL");
        payload.Services.Should().ContainKey("orchestrator");
    }

    [Fact]
    public async Task Health_ReturnsOrchestratorFailureDetail()
    {
        RouterReadinessTestHelpers.ConfigureEnvironment(redisUrl: string.Empty);
        using var factory = RouterReadinessTestHelpers.CreateFactory(
            new RouterReadinessTestHelpers.FailingHttpMessageHandler());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
        payload.Should().NotBeNull();
        payload!.Services.Should().ContainKey("orchestrator");
        payload.Services["orchestrator"].Should().Be(nameof(HttpRequestException));
    }
}
