using System.Net;
using Cwi.Router.Application.Clients;
using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.Pipeline;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Cwi.Router.UnitTests;

[Collection("Env")]
public sealed class CoordinationClientTests
{
    [Fact]
    public async Task ForwardAsync_AllowsCharsetContentType()
    {
        ConfigureEnvironment();
        Environment.SetEnvironmentVariable("COORDINATION_LAYER_URL", "http://coordination.local");

        var handler = new OkHandler();
        var httpClient = new HttpClient(handler);
        var factory = new FakeHttpClientFactory(httpClient);
        var client = new CoordinationClient(factory, RouterConfig.LoadFromEnvironment());

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/mesh/hints";
        httpContext.Request.ContentType = "application/json; charset=utf-8";
        httpContext.TraceIdentifier = "trace";
        var context = new RouterRequestContext(httpContext, "{}");

        var result = await client.ForwardAsync(context, CancellationToken.None);

        result.StatusCode.Should().Be(200);
    }

    private static void ConfigureEnvironment()
    {
        Environment.SetEnvironmentVariable("ROUTER_HTTP_PORT", "8081");
        Environment.SetEnvironmentVariable("ORCHESTRATOR_URL", "http://localhost:8080");
        Environment.SetEnvironmentVariable("AUTH_REQUIRED", "false");
        Environment.SetEnvironmentVariable("MTLS_REQUIRED", "false");
        Environment.SetEnvironmentVariable("REDIS_URL", string.Empty);
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

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public FakeHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name)
        {
            return _client;
        }
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }
}
