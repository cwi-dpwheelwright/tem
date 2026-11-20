using System.Net;
using Cwi.Router.Application.Clients;
using Cwi.Router.Application.Pipeline;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cwi.Router.IntegrationTests;

public sealed class ProxyControllerMethodTests
{
    [Fact]
    public async Task AccountHomeNode_Delete_ForwardsToOrchestrator()
    {
        RouterReadinessTestHelpers.ConfigureEnvironment(redisUrl: string.Empty);
        var orchestratorClient = new CaptureRequestOrchestratorClient();
        using var factory = CreateFactory(orchestratorClient, new OkHttpMessageHandler());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/account/home-node");
        request.Headers.TryAddWithoutValidation("x-tenant-id", "tenant-1");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        orchestratorClient.Method.Should().Be("DELETE");
        orchestratorClient.Path.Should().Be("/api/account/home-node");
    }

    [Fact]
    public async Task AccountConnectors_Get_ForwardsToOrchestrator()
    {
        RouterReadinessTestHelpers.ConfigureEnvironment(redisUrl: string.Empty);
        var orchestratorClient = new CaptureRequestOrchestratorClient();
        using var factory = CreateFactory(orchestratorClient, new OkHttpMessageHandler());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/account/connectors");
        request.Headers.TryAddWithoutValidation("x-tenant-id", "tenant-1");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        orchestratorClient.Method.Should().Be("GET");
        orchestratorClient.Path.Should().Be("/api/account/connectors");
    }

    [Fact]
    public async Task AccountConnector_Patch_ForwardsToOrchestrator()
    {
        RouterReadinessTestHelpers.ConfigureEnvironment(redisUrl: string.Empty);
        var orchestratorClient = new CaptureRequestOrchestratorClient();
        using var factory = CreateFactory(orchestratorClient, new OkHttpMessageHandler());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/account/connectors/conn-123")
        {
            Content = new StringContent("{\"enabled\":true}")
        };
        request.Headers.TryAddWithoutValidation("x-tenant-id", "tenant-1");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        orchestratorClient.Method.Should().Be("PATCH");
        orchestratorClient.Path.Should().Be("/api/account/connectors/conn-123");
    }

    [Fact]
    public async Task AskDeferred_Get_ForwardsToOrchestrator()
    {
        RouterReadinessTestHelpers.ConfigureEnvironment(redisUrl: string.Empty);
        var orchestratorClient = new CaptureRequestOrchestratorClient();
        using var factory = CreateFactory(orchestratorClient, new OkHttpMessageHandler());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/ask/deferred/work-123?request_id=req-123");
        request.Headers.TryAddWithoutValidation("x-tenant-id", "tenant-1");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        orchestratorClient.Method.Should().Be("GET");
        orchestratorClient.Path.Should().Be("/api/ask/deferred/work-123");
        orchestratorClient.QueryString.Should().Be("?request_id=req-123");
    }

    private sealed class CaptureRequestOrchestratorClient : IOrchestratorClient
    {
        public string? Method { get; private set; }
        public string? Path { get; private set; }
        public string? QueryString { get; private set; }

        public Task<RouterResult> ForwardAsync(RouterRequestContext context, CancellationToken cancellationToken)
        {
            Method = context.HttpContext.Request.Method;
            Path = context.HttpContext.Request.Path.Value;
            QueryString = context.HttpContext.Request.QueryString.Value;
            return Task.FromResult(new RouterResult(200, "{}", "application/json"));
        }
    }

    private sealed class OkHttpMessageHandler : HttpMessageHandler
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

    private static WebApplicationFactory<Program> CreateFactory(
        IOrchestratorClient orchestratorClient,
        HttpMessageHandler modelProbeHandler)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton(orchestratorClient);
                    services.AddSingleton<IOrchestratorClient>(orchestratorClient);
                    services.AddHttpClient("model-probe")
                        .ConfigurePrimaryHttpMessageHandler(() => modelProbeHandler);
                });
            });
    }
}
