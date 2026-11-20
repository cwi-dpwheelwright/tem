using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.TrustedWeb;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.TrustedWeb;

public sealed class ConfiguredTrustedWebContextProviderTests
{
    [Fact]
    public async Task RetrieveAsync_fetches_seed_pages_and_extracts_ranked_snippets()
    {
        var profilePath = WriteProfile("""
            {
              "version": 1,
              "default_profile": "general",
              "profiles": {
                "general": {
                  "mode": "trusted_sites_only",
                  "max_results": 3,
                  "sources": [
                    {
                      "source_id": "trusted-docs",
                      "display_name": "Trusted Docs",
                      "allowed_domains": ["trusted.example"],
                      "seed_urls": [
                        "https://trusted.example/alpha",
                        "https://trusted.example/beta",
                        "https://trusted.example/no-match"
                      ]
                    }
                  ]
                }
              }
            }
            """);
        var http = new StubHttpClientFactory(new Dictionary<string, StubResponse>
        {
            ["https://trusted.example/alpha"] = Html("""
                <html>
                  <head><title>Alpha Risk Update</title><script>ignore this risk script</script></head>
                  <body><h1>Alpha Risk Update</h1><p>Oceaneering risk update with practical context for operators.</p></body>
                </html>
                """),
            ["https://trusted.example/beta"] = Html("""
                <html><head><title>Beta Advisory</title></head><body>Oceaneering risk context and background note.</body></html>
                """),
            ["https://trusted.example/no-match"] = Html("""
                <html><head><title>Unrelated</title></head><body>Nothing useful for this query.</body></html>
                """)
        });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new ConfiguredTrustedWebContextProvider(
            http,
            new TrustedWebContextOptions
            {
                ProfilePath = profilePath,
                MaxResults = 2,
                MaxSnippetChars = 90,
                RespectRobots = false
            },
            cache);

        var result = await provider.RetrieveAsync(
            new TrustedWebContextRequest("oceaneering risk update", null, 2),
            CancellationToken.None);

        result.SkippedReason.Should().BeNull();
        result.Profile.Should().Be("general");
        result.Results.Should().HaveCount(2);
        result.Results[0].Url.Should().Be("https://trusted.example/alpha");
        result.Results[0].Title.Should().Be("Alpha Risk Update");
        result.Results[0].Snippet.Should().Contain("Oceaneering risk update");
        result.Results[0].Snippet.Should().NotContain("script");
        result.Results[0].Snippet.Length.Should().BeLessThanOrEqualTo(90);
        result.Results[0].SourceId.Should().Be("trusted-docs");
        result.Results[0].SourceName.Should().Be("Trusted Docs");
        result.Results[0].ChunkId.Should().Be("alpha");
        result.Results.Should().OnlyContain(item => item.Score > 0);
    }

    [Fact]
    public async Task RetrieveAsync_expands_sitemap_and_rss_links_only_inside_allowed_trusted_paths()
    {
        var profilePath = WriteProfile("""
            {
              "version": 1,
              "default_profile": "general",
              "profiles": {
                "general": {
                  "mode": "trusted_sites_only",
                  "sources": [
                    {
                      "source_id": "trusted-news",
                      "display_name": "Trusted News",
                      "allowed_domains": ["trusted.example"],
                      "sitemap_urls": ["https://trusted.example/news/sitemap.xml"],
                      "rss_urls": ["https://trusted.example/news/feed.xml"],
                      "include_path_prefixes": ["/news"],
                      "exclude_path_prefixes": ["/news/archive"]
                    }
                  ]
                }
              }
            }
            """);
        var http = new StubHttpClientFactory(new Dictionary<string, StubResponse>
        {
            ["https://trusted.example/news/sitemap.xml"] = Xml("""
                <urlset>
                  <url><loc>https://trusted.example/news/current</loc></url>
                  <url><loc>https://trusted.example/private/ignored</loc></url>
                  <url><loc>https://trusted.example/news/archive/old</loc></url>
                  <url><loc>https://evil.example/news/current</loc></url>
                </urlset>
                """),
            ["https://trusted.example/news/feed.xml"] = Xml("""
                <rss><channel>
                  <item><link>https://trusted.example/news/feed-item</link></item>
                  <item><link>https://evil.example/news/feed-item</link></item>
                </channel></rss>
                """),
            ["https://trusted.example/news/current"] = Html("""
                <html><head><title>Current Shipping Topics</title></head><body>Shipping topics and current operating context.</body></html>
                """),
            ["https://trusted.example/news/feed-item"] = Html("""
                <html><head><title>Feed Shipping Topics</title></head><body>Shipping topics from the trusted feed.</body></html>
                """)
        });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new ConfiguredTrustedWebContextProvider(
            http,
            new TrustedWebContextOptions
            {
                ProfilePath = profilePath,
                MaxResults = 5,
                RespectRobots = false
            },
            cache);

        var result = await provider.RetrieveAsync(
            new TrustedWebContextRequest("shipping topics", null, 5),
            CancellationToken.None);

        result.SkippedReason.Should().BeNull();
        result.Results.Select(item => item.Url).Should().BeEquivalentTo(new[]
        {
            "https://trusted.example/news/current",
            "https://trusted.example/news/feed-item"
        });
        http.RequestedUris.Should().Contain("https://trusted.example/news/sitemap.xml");
        http.RequestedUris.Should().Contain("https://trusted.example/news/feed.xml");
        http.RequestedUris.Should().NotContain("https://trusted.example/private/ignored");
        http.RequestedUris.Should().NotContain("https://trusted.example/news/archive/old");
        http.RequestedUris.Should().NotContain(uri => uri.StartsWith("https://evil.example/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RetrieveAsync_requires_multiple_matches_for_multi_token_queries()
    {
        var profilePath = WriteProfile("""
            {
              "version": 1,
              "default_profile": "general",
              "profiles": {
                "general": {
                  "mode": "trusted_sites_only",
                  "max_results": 5,
                  "sources": [
                    {
                      "source_id": "trusted-reference",
                      "allowed_domains": ["trusted.example"],
                      "seed_urls": [
                        "https://trusted.example/texas-capital",
                        "https://trusted.example/france-capital"
                      ]
                    }
                  ]
                }
              }
            }
            """);
        var http = new StubHttpClientFactory(new Dictionary<string, StubResponse>
        {
            ["https://trusted.example/texas-capital"] = Html("""
                <html><head><title>Texas Capitol History</title></head><body>Austin is the capital selected for Texas.</body></html>
                """),
            ["https://trusted.example/france-capital"] = Html("""
                <html><head><title>France Capital Facts</title></head><body>Paris is the capital city.</body></html>
                """)
        });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new ConfiguredTrustedWebContextProvider(
            http,
            new TrustedWebContextOptions
            {
                ProfilePath = profilePath,
                MaxResults = 5,
                RespectRobots = false
            },
            cache);

        var result = await provider.RetrieveAsync(
            new TrustedWebContextRequest("capital Texas", null, 5),
            CancellationToken.None);

        result.Results.Should().ContainSingle();
        result.Results[0].Url.Should().Be("https://trusted.example/texas-capital");
    }

    [Fact]
    public async Task RetrieveAsync_respects_robots_disallow_before_fetching_seed_page()
    {
        var profilePath = WriteProfile("""
            {
              "version": 1,
              "default_profile": "general",
              "profiles": {
                "general": {
                  "mode": "trusted_sites_only",
                  "sources": [
                    {
                      "source_id": "trusted-docs",
                      "allowed_domains": ["trusted.example"],
                      "seed_urls": [
                        "https://trusted.example/blocked/compliance",
                        "https://trusted.example/allowed/compliance"
                      ]
                    }
                  ]
                }
              }
            }
            """);
        var http = new StubHttpClientFactory(new Dictionary<string, StubResponse>
        {
            ["https://trusted.example/robots.txt"] = Text("""
                User-agent: *
                Disallow: /blocked
                """),
            ["https://trusted.example/blocked/compliance"] = Html("""
                <html><head><title>Blocked Compliance</title></head><body>Compliance content that robots excludes.</body></html>
                """),
            ["https://trusted.example/allowed/compliance"] = Html("""
                <html><head><title>Allowed Compliance</title></head><body>Compliance content that can be included.</body></html>
                """)
        });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new ConfiguredTrustedWebContextProvider(
            http,
            new TrustedWebContextOptions
            {
                ProfilePath = profilePath,
                MaxResults = 5,
                RespectRobots = true
            },
            cache);

        var result = await provider.RetrieveAsync(
            new TrustedWebContextRequest("compliance content", null, 5),
            CancellationToken.None);

        result.Results.Should().ContainSingle();
        result.Results[0].Url.Should().Be("https://trusted.example/allowed/compliance");
        http.RequestedUris.Should().Contain("https://trusted.example/robots.txt");
        http.RequestedUris.Should().NotContain("https://trusted.example/blocked/compliance");
    }

    [Fact]
    public async Task RetrieveAsync_sends_configured_user_agent_header()
    {
        var profilePath = WriteProfile("""
            {
              "version": 1,
              "default_profile": "general",
              "profiles": {
                "general": {
                  "mode": "trusted_sites_only",
                  "sources": [
                    {
                      "source_id": "trusted-docs",
                      "allowed_domains": ["trusted.example"],
                      "seed_urls": ["https://trusted.example/headers"]
                    }
                  ]
                }
              }
            }
            """);
        var http = new StubHttpClientFactory(new Dictionary<string, StubResponse>
        {
            ["https://trusted.example/headers"] = Html("""
                <html><head><title>Header Smoke</title></head><body>Header smoke trusted context.</body></html>
                """)
        });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new ConfiguredTrustedWebContextProvider(
            http,
            new TrustedWebContextOptions
            {
                ProfilePath = profilePath,
                RespectRobots = false,
                UserAgent = "CWI-TrustedWebTests/1.0"
            },
            cache);

        var result = await provider.RetrieveAsync(
            new TrustedWebContextRequest("header smoke", null, 1),
            CancellationToken.None);

        result.Results.Should().ContainSingle();
        http.RequestedUserAgents.Should().Contain(userAgent => userAgent.Contains("CWI-TrustedWebTests/1.0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RetrieveAsync_returns_skipped_reason_for_missing_or_unsupported_profile()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var missingProvider = new ConfiguredTrustedWebContextProvider(
            new StubHttpClientFactory(new Dictionary<string, StubResponse>()),
            new TrustedWebContextOptions
            {
                ProfilePath = Path.Combine(Path.GetTempPath(), $"missing-trusted-web-{Guid.NewGuid():N}.json")
            },
            cache);

        var missing = await missingProvider.RetrieveAsync(
            new TrustedWebContextRequest("anything", null, 5),
            CancellationToken.None);

        missing.Results.Should().BeEmpty();
        missing.SkippedReason.Should().Be("trusted_web_profile_not_configured");

        var profilePath = WriteProfile("""
            {
              "version": 1,
              "default_profile": "general",
              "profiles": {
                "general": {
                  "mode": "paid_provider",
                  "sources": [
                    {
                      "source_id": "trusted-docs",
                      "allowed_domains": ["trusted.example"],
                      "seed_urls": ["https://trusted.example/alpha"]
                    }
                  ]
                }
              }
            }
            """);
        using var unsupportedCache = new MemoryCache(new MemoryCacheOptions());
        var http = new StubHttpClientFactory(new Dictionary<string, StubResponse>
        {
            ["https://trusted.example/alpha"] = Html("<html><body>anything</body></html>")
        });
        var unsupportedProvider = new ConfiguredTrustedWebContextProvider(
            http,
            new TrustedWebContextOptions { ProfilePath = profilePath },
            unsupportedCache);

        var unsupported = await unsupportedProvider.RetrieveAsync(
            new TrustedWebContextRequest("anything", null, 5),
            CancellationToken.None);

        unsupported.Results.Should().BeEmpty();
        unsupported.SkippedReason.Should().Be("trusted_web_profile_mode_unsupported");
        http.RequestedUris.Should().BeEmpty();
    }

    private static string WriteProfile(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"trusted-web-profile-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static StubResponse Html(string body) => new(body, "text/html");

    private static StubResponse Xml(string body) => new(body, "application/xml");

    private static StubResponse Text(string body) => new(body, "text/plain");

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly StubHttpMessageHandler _handler;

        public StubHttpClientFactory(IReadOnlyDictionary<string, StubResponse> responses)
        {
            _handler = new StubHttpMessageHandler(responses);
        }

        public IReadOnlyList<string> RequestedUris => _handler.RequestedUris;

        public IReadOnlyList<string> RequestedUserAgents => _handler.RequestedUserAgents;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, StubResponse> _responses;
        private readonly List<string> _requestedUris = new();
        private readonly List<string> _requestedUserAgents = new();

        public StubHttpMessageHandler(IReadOnlyDictionary<string, StubResponse> responses)
        {
            _responses = responses;
        }

        public IReadOnlyList<string> RequestedUris => _requestedUris;

        public IReadOnlyList<string> RequestedUserAgents => _requestedUserAgents;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            _requestedUris.Add(uri);
            _requestedUserAgents.Add(request.Headers.UserAgent.ToString());

            if (!_responses.TryGetValue(uri, out var response))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(string.Empty)
                });
            }

            return Task.FromResult(response.ToHttpResponseMessage());
        }
    }

    private sealed record StubResponse(string Body, string ContentType)
    {
        public HttpResponseMessage ToHttpResponseMessage()
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Body, System.Text.Encoding.UTF8, ContentType)
            };
        }
    }
}
