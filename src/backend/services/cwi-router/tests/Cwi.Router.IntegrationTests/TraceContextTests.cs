using System.Linq;
using System.Net;
using System.Text;
using Cwi.Router.Application.Clients;
using Cwi.Router.Application.Pipeline;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cwi.Router.IntegrationTests;

public sealed class TraceContextTests
{
    [Fact]
    public async Task Traceparent_Passthroughs_To_Orchestrator()
    {
        RouterReadinessTestHelpers.ConfigureEnvironment(redisUrl: string.Empty);
        var orchestratorClient = new CaptureOrchestratorClient();
        using var factory = CreateFactory(orchestratorClient, new OkHttpMessageHandler());
        using var client = factory.CreateClient();

        const string traceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/ask")
        {
            Content = new StringContent("{\"tenant_id\":\"tenant-1\"}", Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("traceparent", traceParent);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        orchestratorClient.TraceParent.Should().Be(traceParent);

        var expectedTraceId = ExtractTraceId(traceParent);
        orchestratorClient.RequestId.Should().Be(expectedTraceId);
        response.Headers.TryGetValues("X-Request-Id", out var requestIdValues).Should().BeTrue();
        requestIdValues!.Single().Should().Be(expectedTraceId);
    }

    [Fact]
    public async Task Traceparent_Generated_When_Missing()
    {
        RouterReadinessTestHelpers.ConfigureEnvironment(redisUrl: string.Empty);
        var orchestratorClient = new CaptureOrchestratorClient();
        using var factory = CreateFactory(orchestratorClient, new OkHttpMessageHandler());
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/ask")
        {
            Content = new StringContent("{\"tenant_id\":\"tenant-1\"}", Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        orchestratorClient.TraceParent.Should().NotBeNullOrWhiteSpace();
        var traceParent = orchestratorClient.TraceParent!;
        traceParent.Should().MatchRegex("^[0-9a-f]{2}-[0-9a-f]{32}-[0-9a-f]{16}-[0-9a-f]{2}$");

        var expectedTraceId = ExtractTraceId(traceParent);
        orchestratorClient.RequestId.Should().Be(expectedTraceId);
        response.Headers.TryGetValues("X-Request-Id", out var requestIdValues).Should().BeTrue();
        requestIdValues!.Single().Should().Be(expectedTraceId);
    }

    private static string ExtractTraceId(string traceParent)
    {
        var parts = traceParent.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1] : string.Empty;
    }

    private sealed class CaptureOrchestratorClient : IOrchestratorClient
    {
        public string? TraceParent { get; private set; }
        public string? RequestId { get; private set; }

        public Task<RouterResult> ForwardAsync(RouterRequestContext context, CancellationToken cancellationToken)
        {
            TraceParent = context.HttpContext.Request.Headers["traceparent"].ToString();
            RequestId = context.HttpContext.Request.Headers["X-Request-Id"].ToString();
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
