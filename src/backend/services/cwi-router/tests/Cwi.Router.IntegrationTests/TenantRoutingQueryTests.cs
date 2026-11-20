using System.Net;
using Cwi.Router.Application.Clients;
using Cwi.Router.Application.Pipeline;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cwi.Router.IntegrationTests;

public sealed class TenantRoutingQueryTests
{
    [Fact]
    public async Task TenantId_QueryParam_Passes_TenantRouting()
    {
        RouterReadinessTestHelpers.ConfigureEnvironment(redisUrl: string.Empty);
        var orchestratorClient = new CaptureTenantOrchestratorClient();
        using var factory = CreateFactory(orchestratorClient, new OkHttpMessageHandler());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/reports?tenant_id=tenant-99");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        orchestratorClient.TenantId.Should().Be("tenant-99");
    }

    private sealed class CaptureTenantOrchestratorClient : IOrchestratorClient
    {
        public string? TenantId { get; private set; }

        public Task<RouterResult> ForwardAsync(RouterRequestContext context, CancellationToken cancellationToken)
        {
            TenantId = context.TenantId;
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
