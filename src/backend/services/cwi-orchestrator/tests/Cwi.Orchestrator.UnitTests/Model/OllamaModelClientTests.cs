using System.Net;
using System.Text;
using System.Text.Json;
using Cwi.Orchestrator.Application;
using Cwi.Orchestrator.Application.Model;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Model;

public sealed class OllamaModelClientTests
{
    [Fact]
    public async Task GenerateAsync_sends_bounded_generation_options()
    {
        JsonDocument? capturedRequest = null;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            capturedRequest = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"model":"qwen2.5:14b","response":"Short answer."}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }));

        var client = CreateClient(http, modelMaxOutputTokens: 256);

        var result = await client.GenerateAsync(
            new ModelRequest("Give me a short checklist.", RequestedModel: null, RequestedTier: "free"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        var root = capturedRequest!.RootElement;
        root.GetProperty("model").GetString().Should().Be("qwen2.5:7b");
        root.GetProperty("stream").GetBoolean().Should().BeFalse();
        root.GetProperty("options").GetProperty("num_predict").GetInt32().Should().Be(256);
        root.GetProperty("options").GetProperty("temperature").GetDouble().Should().Be(0.2);
    }

    [Fact]
    public async Task GenerateAsync_returns_dependency_failure_when_model_times_out()
    {
        using var http = new HttpClient(new StubHandler(_ => Task.FromException<HttpResponseMessage>(
            new TaskCanceledException("timeout"))));
        var client = CreateClient(http, modelMaxOutputTokens: 256);

        var result = await client.GenerateAsync(
            new ModelRequest("This should timeout.", RequestedModel: null, RequestedTier: "free"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(503);
        result.Error!.Code.Should().Be("dependency_unavailable");
    }

    private static OllamaModelClient CreateClient(HttpClient httpClient, int modelMaxOutputTokens)
    {
        var options = new ApplicationOptions(
            packsBaseUrl: "http://packs.local/",
            packsDirectory: null,
            graphEnabled: false,
            graphDbConnectionString: "Host=localhost;Database=cwi",
            graphName: "cwi_graph",
            graphQueryTimeoutSeconds: 5,
            graphTopK: 5,
            vectorShard1Url: "http://vector1.local/",
            vectorShard2Url: "http://vector2.local/",
            vectorEmbeddingDimension: 384,
            modelBaseUrl: "http://model.local/",
            modelName: "qwen2.5:14b",
            modelTimeoutSeconds: 30,
            policyVersion: "v1",
            ledgerHashSuiteId: "sha256-v1",
            modelBaseUrlFree: "http://free-model.local/",
            modelNameFree: "qwen2.5:7b",
            modelMaxOutputTokens: modelMaxOutputTokens);

        return new OllamaModelClient(options, new StaticHttpClientFactory(httpClient), new ModelTargetResolver(options));
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _httpClient;

        public StaticHttpClientFactory(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public HttpClient CreateClient(string name)
        {
            name.Should().Be("model");
            return _httpClient;
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this(request => Task.FromResult(handler(request)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsolutePath.Should().Be("/api/generate");
            return _handler(request);
        }
    }
}
