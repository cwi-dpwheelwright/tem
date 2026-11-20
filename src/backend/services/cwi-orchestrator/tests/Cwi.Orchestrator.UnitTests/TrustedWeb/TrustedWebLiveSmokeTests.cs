using Cwi.Orchestrator.Application.TrustedWeb;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Xunit;
using Xunit.Abstractions;

namespace Cwi.Orchestrator.UnitTests.TrustedWeb;

public sealed class TrustedWebLiveSmokeTests
{
    private readonly ITestOutputHelper _output;

    public TrustedWebLiveSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "LiveSmoke")]
    public async Task Live_configured_seed_sites_return_scored_results()
    {
        if (!LiveSmokeEnabled())
        {
            _output.WriteLine("Set CWI_TRUSTED_WEB_LIVE_SMOKE=1 to run live trusted-web smoke checks.");
            return;
        }

        var sites = new[]
        {
            new SmokeSite(
                "texas-state-preservation-board-capitol-history",
                "Texas State Preservation Board - Capitol History",
                "https://www.tspb.texas.gov/prop/tc/tc-history/history/",
                "capital Texas Austin",
                "tspb.texas.gov"),
            new SmokeSite(
                "britannica-france",
                "Britannica - France",
                "https://www.britannica.com/place/France",
                "capital France Paris",
                "britannica.com"),
            new SmokeSite(
                "wikipedia-austin-texas",
                "Wikipedia - Austin, Texas",
                "https://en.wikipedia.org/wiki/Austin,_Texas",
                "capital Texas Austin",
                "wikipedia.org"),
            new SmokeSite(
                "example-domain",
                "Example Domain",
                "https://example.com/",
                "example domain",
                "example.com"),
            new SmokeSite(
                "iana-example-domains",
                "IANA Example Domains",
                "https://www.iana.org/help/example-domains",
                "example domains documentation",
                "iana.org"),
            new SmokeSite(
                "microsoft-learn-dotnet",
                "Microsoft Learn .NET",
                "https://learn.microsoft.com/en-us/dotnet/core/introduction",
                "dotnet cross platform open source",
                "learn.microsoft.com")
        };
        var failures = new List<string>();

        foreach (var site in sites)
        {
            try
            {
                using var cache = new MemoryCache(new MemoryCacheOptions());
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var provider = new ConfiguredTrustedWebContextProvider(
                    new LiveHttpClientFactory(),
                    new TrustedWebContextOptions
                    {
                        MaxResults = 1,
                        MaxSnippetChars = 400,
                        CacheTtlSeconds = 0,
                        FetchTimeoutMs = 15000,
                        RespectRobots = true,
                        UserAgent = "CWI-TrustedWebLiveSmoke/1.0"
                    },
                    cache,
                    new InlineTrustedWebConfigStore(BuildDocument(site)));

                var result = await provider.RetrieveAsync(
                    new TrustedWebContextRequest(site.Query, "general", 1, "live-smoke"),
                    cts.Token);

                if (!string.IsNullOrWhiteSpace(result.SkippedReason))
                {
                    failures.Add($"{site.SourceId}: skipped with {result.SkippedReason}");
                    continue;
                }

                var hit = result.Results.FirstOrDefault();
                if (hit is null)
                {
                    failures.Add($"{site.SourceId}: returned no results");
                    continue;
                }

                if (!Uri.TryCreate(hit.Url, UriKind.Absolute, out var hitUri) ||
                    !hitUri.Host.EndsWith(site.AllowedDomain, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"{site.SourceId}: returned non-allowlisted URL '{hit.Url}'");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(hit.Title) ||
                    string.IsNullOrWhiteSpace(hit.Snippet) ||
                    hit.Score <= 0)
                {
                    failures.Add($"{site.SourceId}: returned incomplete result title/snippet/score");
                    continue;
                }

                _output.WriteLine($"{site.SourceId}: {hit.Title} | {hit.Url} | score={hit.Score:0.00}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                failures.Add($"{site.SourceId}: {ex.GetType().Name} {ex.Message}");
            }
        }

        failures.Should().BeEmpty("each live trusted-web seed should return a scored allowlisted result");
    }

    private static bool LiveSmokeEnabled()
    {
        var value = Environment.GetEnvironmentVariable("CWI_TRUSTED_WEB_LIVE_SMOKE");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static TrustedWebProfilesDocument BuildDocument(SmokeSite site)
    {
        return new TrustedWebProfilesDocument
        {
            Version = 1,
            DefaultProfile = "general",
            Profiles = new Dictionary<string, TrustedWebProfile>(StringComparer.OrdinalIgnoreCase)
            {
                ["general"] = new()
                {
                    Mode = "trusted_sites_only",
                    MaxResults = 1,
                    Sources = new[]
                    {
                        new TrustedWebSource
                        {
                            SourceId = site.SourceId,
                            DisplayName = site.DisplayName,
                            AllowedDomains = new[] { site.AllowedDomain },
                            SeedUrls = new[] { site.Url }
                        }
                    }
                }
            }
        };
    }

    private sealed record SmokeSite(
        string SourceId,
        string DisplayName,
        string Url,
        string Query,
        string AllowedDomain);

    private sealed class InlineTrustedWebConfigStore : ITrustedWebConfigStore
    {
        private readonly TrustedWebProfilesDocument _document;

        public InlineTrustedWebConfigStore(TrustedWebProfilesDocument document)
        {
            _document = document;
        }

        public Task<TrustedWebRuntimeConfig> LoadRuntimeConfigAsync(
            string? tenantId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new TrustedWebRuntimeConfig(_document, DatabaseConfigured: true));
        }
    }

    private sealed class LiveHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
        }
    }
}
