using System.Net;
using System.Text;
using System.Text.Json;
using Cwi.Orchestrator.Application.Retrieval;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Retrieval;

public sealed class VectorShardClientTests
{
    [Fact]
    public async Task SearchAsync_includes_pack_hash_filter_when_present()
    {
        var handler = new CaptureHandler();
        var client = new VectorShard1Client(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://vector.local/")
        });

        var result = await client.SearchAsync(
            "cwi_corpus",
            new[] { 0.1f, 0.2f },
            3,
            new RetrievalFilter
            {
                PackId = "operations-pack",
                PackHash = "hash-123",
                PackVersion = "1.0.0"
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        handler.LastRequestBody.Should().NotBeNullOrWhiteSpace();

        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        var root = document.RootElement;
        var must = root.GetProperty("filter").GetProperty("must");
        must.GetArrayLength().Should().Be(2);
        must[0].GetProperty("key").GetString().Should().Be("pack_id");
        must[0].GetProperty("match").GetProperty("value").GetString().Should().Be("operations-pack");
        must[1].GetProperty("key").GetString().Should().Be("pack_hash");
        must[1].GetProperty("match").GetProperty("value").GetString().Should().Be("hash-123");
    }

    [Fact]
    public async Task SearchAsync_falls_back_to_pack_version_filter_when_hash_missing()
    {
        var handler = new CaptureHandler();
        var client = new VectorShard1Client(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://vector.local/")
        });

        var result = await client.SearchAsync(
            "cwi_corpus",
            new[] { 0.1f, 0.2f },
            3,
            new RetrievalFilter
            {
                PackId = "operations-pack",
                PackVersion = "1.0.0"
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        var must = document.RootElement.GetProperty("filter").GetProperty("must");
        must.GetArrayLength().Should().Be(2);
        must[1].GetProperty("key").GetString().Should().Be("pack_version");
        must[1].GetProperty("match").GetProperty("value").GetString().Should().Be("1.0.0");
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":[]}", Encoding.UTF8, "application/json")
            };
        }
    }
}
