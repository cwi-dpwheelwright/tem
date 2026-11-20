using System.Net;
using Cwi.Orchestrator.Application.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Http;

public sealed class ResilienceHandlerTests
{
    [Theory]
    [InlineData("POST", 503)][InlineData("POST", 408)][InlineData("POST", 429)]
    [InlineData("PUT", 503)][InlineData("PUT", 408)][InlineData("PUT", 429)]
    [InlineData("PATCH", 503)][InlineData("PATCH", 408)][InlineData("PATCH", 429)]
    [InlineData("DELETE", 503)][InlineData("DELETE", 408)][InlineData("DELETE", 429)]
    public async Task Non_read_response_never_authorizes_transport_replay(string method, int status)
    {
        var inner = new CountingHandler(_ => new HttpResponseMessage((HttpStatusCode)status));
        using var client = new HttpClient(CreateHandler(inner));
        using var request = new HttpRequestMessage(new HttpMethod(method), SyntheticUrl());
        using var response = await client.SendAsync(request);
        inner.CallCount.Should().Be(1);
        ((int)response.StatusCode).Should().Be(status);
    }

    [Theory]
    [InlineData("POST")][InlineData("PUT")][InlineData("PATCH")][InlineData("DELETE")]
    public async Task Lost_write_acknowledgement_is_propagated_without_replay(string method)
    {
        var inner = new CountingHandler(_ => throw new HttpRequestException("synthetic lost acknowledgement"));
        using var client = new HttpClient(CreateHandler(inner));
        using var request = new HttpRequestMessage(new HttpMethod(method), SyntheticUrl());
        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(request));
        inner.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Cloning_preserves_typed_request_options_for_downstream_governance()
    {
        var key = new HttpRequestOptionsKey<string>("synthetic-governance-context");
        var inner = new CountingHandler(request =>
        {
            request.Options.TryGetValue(key, out var value).Should().BeTrue();
            value.Should().Be("synthetic-scope");
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(CreateHandler(inner));
        using var request = new HttpRequestMessage(HttpMethod.Post, SyntheticUrl());
        request.Options.Set(key, "synthetic-scope");
        using var response = await client.SendAsync(request);
        inner.CallCount.Should().Be(1);
    }

    private static string SyntheticUrl() => $"https://synthetic-{Guid.NewGuid():N}.invalid/test";

    [Fact]
    public async Task SendAsync_Retries_OnServerErrors()
    {
        var inner = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var handler = CreateHandler(inner);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("http://retry-host.local/test");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        inner.CallCount.Should().Be(3);
    }

    [Fact]
    public async Task SendAsync_OpensCircuit_AfterFailures()
    {
        var inner = new CountingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var handler = CreateHandler(inner);
        using var client = new HttpClient(handler);

        await client.GetAsync("http://circuit-host.local/test");
        await client.GetAsync("http://circuit-host.local/test");
        await client.GetAsync("http://circuit-host.local/test");

        var response = await client.GetAsync("http://circuit-host.local/test");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.ReasonPhrase.Should().Be("Circuit open");
    }

    private static ResilienceHandler CreateHandler(HttpMessageHandler inner)
    {
        var loggerFactory = LoggerFactory.Create(builder => { });
        var handler = new ResilienceHandler(loggerFactory.CreateLogger<ResilienceHandler>())
        {
            InnerHandler = inner
        };
        return handler;
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public int CallCount { get; private set; }

        public CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_responder(request));
        }
    }
}
