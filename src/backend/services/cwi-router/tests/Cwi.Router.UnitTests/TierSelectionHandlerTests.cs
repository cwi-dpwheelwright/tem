using Cwi.Common;
using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.Health;
using Cwi.Router.Application.Handlers;
using Cwi.Router.Application.Pipeline;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cwi.Router.UnitTests;

[Collection("Env")]
public sealed class TierSelectionHandlerTests
{
    [Fact]
    public async Task ProTier_SelectsDeterministicEndpoint()
    {
        ConfigureEnvironment(
            modelPro: "http://pro-1.local,http://pro-2.local",
            modelFree: "http://free-1.local");

        var config = RouterConfig.LoadFromEnvironment();
        var registry = new ModelEndpointRegistry(
            config,
            new ModelEndpointCircuitBreaker(),
            NullLogger<ModelEndpointRegistry>.Instance);
        var handler = new TierSelectionHandler(
            config,
            registry,
            NullLogger<TierSelectionHandler>.Instance);

        var first = await ExecuteAsync(handler, "tenant-1", "trace-1", tier: "pro");
        var second = await ExecuteAsync(handler, "tenant-1", "trace-1", tier: "pro");

        first.Should().NotBeNull();
        second.Should().Be(first);
    }

    [Fact]
    public async Task ProTier_FallsBackToFree_WhenProEndpointsUnhealthy()
    {
        ConfigureEnvironment(
            modelPro: "http://pro-1.local,http://pro-2.local",
            modelFree: "http://free-1.local",
            failureThreshold: "1");

        var config = RouterConfig.LoadFromEnvironment();
        var registry = new ModelEndpointRegistry(
            config,
            new ModelEndpointCircuitBreaker(),
            NullLogger<ModelEndpointRegistry>.Instance);
        var handler = new TierSelectionHandler(
            config,
            registry,
            NullLogger<TierSelectionHandler>.Instance);

        var now = DateTimeOffset.UtcNow;
        foreach (var endpoint in registry.GetEndpoints(ModelTier.Pro))
        {
            registry.RecordFailure(endpoint, now, "probe failed");
        }

        var selected = await ExecuteAsync(handler, "tenant-1", "trace-1", tier: "pro");

        selected.Should().Be(new Uri("http://free-1.local"));
    }

    private static async Task<Uri?> ExecuteAsync(
        TierSelectionHandler handler,
        string tenantId,
        string traceId,
        string tier)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["x-model-tier"] = tier;
        httpContext.TraceIdentifier = traceId;

        var context = new RouterRequestContext(httpContext, body: string.Empty)
        {
            TenantId = tenantId
        };

        var response = await handler.HandleAsync(
            context,
            () => Task.FromResult(ServiceResponse<RouterResult>.Success(new RouterResult(200, "{}", "application/json"))),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        return context.ModelUrl;
    }

    private static void ConfigureEnvironment(
        string modelPro,
        string modelFree,
        string? failureThreshold = null)
    {
        Environment.SetEnvironmentVariable("ORCHESTRATOR_URL", "http://localhost:8080");
        Environment.SetEnvironmentVariable("MODEL_PRO_URLS", modelPro);
        Environment.SetEnvironmentVariable("MODEL_FREE_URLS", modelFree);
        Environment.SetEnvironmentVariable("MODEL_CB_FAILURE_THRESHOLD", failureThreshold ?? "3");
    }
}
