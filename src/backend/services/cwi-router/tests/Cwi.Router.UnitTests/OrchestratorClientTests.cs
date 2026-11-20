using System.Net;
using System.Text;
using Cwi.Router.Application.Clients;
using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.EventBus;
using Cwi.Router.Application.Journey;
using Cwi.Router.Application.Pipeline;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cwi.Router.UnitTests;

[Collection("Env")]
public sealed class OrchestratorClientTests
{
    [Fact]
    public async Task FallsBackToNextEndpointOnServerError()
    {
        ConfigureEnvironment();
        Environment.SetEnvironmentVariable("ORCHESTRATOR_URL", "http://primary.local");
        Environment.SetEnvironmentVariable("ORCHESTRATOR_URLS", "http://primary.local,http://secondary.local");

        var handler = new EndpointAwareHandler();
        var httpClient = new HttpClient(handler);
        var factory = new FakeHttpClientFactory(httpClient);
        var journeyEmitter = CreateJourneyEmitter();
        var client = new OrchestratorClient(factory, RouterConfig.LoadFromEnvironment(), journeyEmitter);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/ask";
        httpContext.Request.ContentType = "application/json";
        httpContext.TraceIdentifier = "trace";
        var context = new RouterRequestContext(httpContext, "{\"question\":\"hi\"}");

        var result = await client.ForwardAsync(context, CancellationToken.None);

        result.StatusCode.Should().Be(200);
        handler.RequestedHosts.Should().ContainInOrder(new[] { "primary.local", "secondary.local" });
    }

    [Fact]
    public async Task RetryDecoratorAddsIdempotencyKey()
    {
        var attempts = 0;
        var inner = new DelegateOrchestratorClient((context, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new HttpRequestException("boom");
            }

            return Task.FromResult(new RouterResult(200, "{}", "application/json"));
        });

        var decorator = new OrchestratorClientRetryDecorator(inner, maxAttempts: 2, delay: TimeSpan.Zero);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.TraceIdentifier = "trace";
        var context = new RouterRequestContext(httpContext, "{}");

        var result = await decorator.ForwardAsync(context, CancellationToken.None);

        result.StatusCode.Should().Be(200);
        attempts.Should().Be(2);
        context.HttpContext.Request.Headers["X-Idempotency-Key"].ToString().Should().Be("trace");
    }

    [Fact]
    public async Task ForwardAsync_AllowsCharsetContentType()
    {
        ConfigureEnvironment();
        Environment.SetEnvironmentVariable("ORCHESTRATOR_URL", "http://primary.local");

        var handler = new OkHandler();
        var httpClient = new HttpClient(handler);
        var factory = new FakeHttpClientFactory(httpClient);
        var journeyEmitter = CreateJourneyEmitter();
        var client = new OrchestratorClient(factory, RouterConfig.LoadFromEnvironment(), journeyEmitter);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/ask";
        httpContext.Request.ContentType = "application/json; charset=utf-8";
        httpContext.TraceIdentifier = "trace";
        var context = new RouterRequestContext(httpContext, "{\"question\":\"hi\"}");

        var result = await client.ForwardAsync(context, CancellationToken.None);

        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task ForwardAsync_ReturnsStreamingResult_ForEventStreamResponses()
    {
        ConfigureEnvironment();
        Environment.SetEnvironmentVariable("ORCHESTRATOR_URL", "http://primary.local");

        var handler = new StreamingHandler();
        var httpClient = new HttpClient(handler);
        var factory = new FakeHttpClientFactory(httpClient);
        var journeyEmitter = CreateJourneyEmitter();
        var client = new OrchestratorClient(factory, RouterConfig.LoadFromEnvironment(), journeyEmitter);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/api/journey/requests/request-1/stream";
        httpContext.TraceIdentifier = "trace";
        var context = new RouterRequestContext(httpContext, string.Empty);

        var result = await client.ForwardAsync(context, CancellationToken.None);

        result.StatusCode.Should().Be(200);
        result.IsStream.Should().BeTrue();
        result.ContentType.Should().StartWith("text/event-stream");
        result.BodyStream.Should().NotBeNull();
    }

    private static RouterJourneyEmitter CreateJourneyEmitter()
    {
        var context = new JourneyRequestContext
        {
            RequestId = "request-1",
            TraceId = "trace-1",
            TenantId = "tenant-1",
            UserId = "user-1",
            Method = "POST",
            Path = "/api/ask"
        };
        var publisher = new EventBusPublisher(
            new EventBusOptions { Enabled = false },
            NullLogger<EventBusPublisher>.Instance);
        return new RouterJourneyEmitter(context, publisher);
    }

    private static void ConfigureEnvironment()
    {
        Environment.SetEnvironmentVariable("ROUTER_HTTP_PORT", "8081");
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

    private sealed class EndpointAwareHandler : HttpMessageHandler
    {
        public List<string> RequestedHosts { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var host = request.RequestUri?.Host ?? string.Empty;
            RequestedHosts.Add(host);

            if (string.Equals(host, "primary.local", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("fail")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}")
            });
        }
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }

    private sealed class StreamingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes("data: {}\n\n")))
            };
            response.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(response);
        }
    }

    private sealed class DelegateOrchestratorClient : IOrchestratorClient
    {
        private readonly Func<RouterRequestContext, CancellationToken, Task<RouterResult>> _handler;

        public DelegateOrchestratorClient(Func<RouterRequestContext, CancellationToken, Task<RouterResult>> handler)
        {
            _handler = handler;
        }

        public Task<RouterResult> ForwardAsync(RouterRequestContext context, CancellationToken cancellationToken)
        {
            return _handler(context, cancellationToken);
        }
    }
}
