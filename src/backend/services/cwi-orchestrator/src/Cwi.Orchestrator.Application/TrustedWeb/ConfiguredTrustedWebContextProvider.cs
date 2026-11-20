#nullable enable
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace Cwi.Orchestrator.Application.TrustedWeb;

public sealed class ConfiguredTrustedWebContextProvider : ITrustedWebContextProvider
{
    private const int MaxUrlsPerRequest = 24;
    private const int MaxFeedLinks = 20;
    private const int MaxRemoteChars = 500_000;
    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex ScriptStyleRegex = new("<(script|style)[^>]*>.*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex TitleRegex = new("<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex H1Regex = new("<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex LocRegex = new("<loc>\\s*(.*?)\\s*</loc>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex RssLinkRegex = new("<link[^>]*>\\s*(https?://.*?)\\s*</link>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex AtomLinkRegex = new("<link[^>]+href=[\"'](https?://[^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly HashSet<string> QueryStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about",
        "after",
        "again",
        "also",
        "and",
        "are",
        "can",
        "could",
        "current",
        "does",
        "for",
        "from",
        "how",
        "into",
        "latest",
        "news",
        "now",
        "right",
        "that",
        "the",
        "this",
        "today",
        "what",
        "when",
        "where",
        "who",
        "why",
        "with"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TrustedWebContextOptions _options;
    private readonly IMemoryCache _cache;
    private readonly ITrustedWebConfigStore _configStore;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ConfiguredTrustedWebContextProvider(
        IHttpClientFactory httpClientFactory,
        TrustedWebContextOptions options,
        IMemoryCache cache)
        : this(httpClientFactory, options, cache, NullTrustedWebConfigStore.Instance)
    {
    }

    public ConfiguredTrustedWebContextProvider(
        IHttpClientFactory httpClientFactory,
        TrustedWebContextOptions options,
        IMemoryCache cache,
        ITrustedWebConfigStore configStore)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _cache = cache;
        _configStore = configStore;
    }

    public async Task<TrustedWebContextResult> RetrieveAsync(
        TrustedWebContextRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new TrustedWebContextResult(Array.Empty<TrustedWebResult>(), ResolveProfileName(request), "trusted_web_empty_query");
        }

        var loadedProfiles = await LoadProfilesAsync(request.TenantId, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(loadedProfiles.SkippedReason))
        {
            return new TrustedWebContextResult(Array.Empty<TrustedWebResult>(), ResolveProfileName(request), loadedProfiles.SkippedReason);
        }

        var profiles = loadedProfiles.Profiles;
        if (profiles is null || profiles.Profiles.Count == 0)
        {
            return new TrustedWebContextResult(Array.Empty<TrustedWebResult>(), ResolveProfileName(request), "trusted_web_profile_not_configured");
        }

        var profileName = ResolveProfileName(request, profiles);
        var profileEntry = profiles.Profiles.FirstOrDefault(pair =>
            string.Equals(pair.Key, profileName, StringComparison.OrdinalIgnoreCase));
        var profile = profileEntry.Value;
        if (profile is null)
        {
            return new TrustedWebContextResult(Array.Empty<TrustedWebResult>(), profileName, "trusted_web_profile_not_found");
        }

        if (!string.Equals(profile.Mode, "trusted_sites_only", StringComparison.OrdinalIgnoreCase))
        {
            return new TrustedWebContextResult(Array.Empty<TrustedWebResult>(), profileName, "trusted_web_profile_mode_unsupported");
        }

        if (profile.Sources.Count == 0)
        {
            return new TrustedWebContextResult(Array.Empty<TrustedWebResult>(), profileName, "trusted_web_profile_empty");
        }

        var maxResults = ClampResults(request.MaxResults ?? profile.MaxResults ?? _options.MaxResults);
        var queryTokens = Tokenize(request.Query).ToArray();
        var candidates = new List<TrustedWebResult>();
        foreach (var source in profile.Sources)
        {
            var urls = await ResolveCandidateUrlsAsync(source, cancellationToken).ConfigureAwait(false);
            foreach (var url in urls.Take(MaxUrlsPerRequest))
            {
                var body = await FetchContentAsync(url, respectRobots: true, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(body))
                {
                    continue;
                }

                var result = BuildResult(source, url, body, queryTokens);
                if (result is not null)
                {
                    candidates.Add(result);
                }
            }
        }

        var results = candidates
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.SourceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Title, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToArray();

        return new TrustedWebContextResult(results, profileName);
    }

    private async Task<TrustedWebRuntimeConfig> LoadProfilesAsync(
        string? tenantId,
        CancellationToken cancellationToken)
    {
        var runtimeConfig = await _configStore.LoadRuntimeConfigAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (runtimeConfig.DatabaseConfigured || runtimeConfig.Profiles is not null || !string.IsNullOrWhiteSpace(runtimeConfig.SkippedReason))
        {
            return runtimeConfig;
        }

        return new TrustedWebRuntimeConfig(LoadFileProfiles());
    }

    private TrustedWebProfilesDocument? LoadFileProfiles()
    {
        var cacheKey = $"trusted-web:profiles:{_options.ProfilePath}";
        if (_cache.TryGetValue(cacheKey, out TrustedWebProfilesDocument? cached))
        {
            return cached;
        }

        if (string.IsNullOrWhiteSpace(_options.ProfilePath) || !File.Exists(_options.ProfilePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_options.ProfilePath);
            var document = JsonSerializer.Deserialize<TrustedWebProfilesDocument>(json, _jsonOptions);
            if (document is not null && _options.CacheTtlSeconds > 0)
            {
                _cache.Set(cacheKey, document, TimeSpan.FromSeconds(Math.Min(_options.CacheTtlSeconds, 300)));
            }

            return document;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<Uri>> ResolveCandidateUrlsAsync(
        TrustedWebSource source,
        CancellationToken cancellationToken)
    {
        var urls = new List<Uri>();
        AddConfiguredUrls(urls, source.SeedUrls, source);

        foreach (var sitemap in source.SitemapUrls)
        {
            if (!TryCreateAllowedUri(sitemap, source, out var sitemapUri))
            {
                continue;
            }

            var body = await FetchContentAsync(sitemapUri, respectRobots: false, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            foreach (Match match in LocRegex.Matches(body).Take(MaxFeedLinks))
            {
                var value = WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
                if (TryCreateAllowedUri(value, source, out var uri))
                {
                    urls.Add(uri);
                }
            }
        }

        foreach (var feed in source.RssUrls)
        {
            if (!TryCreateAllowedUri(feed, source, out var feedUri))
            {
                continue;
            }

            var body = await FetchContentAsync(feedUri, respectRobots: false, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            foreach (var uri in ExtractFeedLinks(body, source).Take(MaxFeedLinks))
            {
                urls.Add(uri);
            }
        }

        return urls
            .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .Take(MaxUrlsPerRequest)
            .ToArray();
    }

    private static void AddConfiguredUrls(List<Uri> urls, IEnumerable<string> configuredUrls, TrustedWebSource source)
    {
        foreach (var value in configuredUrls)
        {
            if (TryCreateAllowedUri(value, source, out var uri))
            {
                urls.Add(uri);
            }
        }
    }

    private static IEnumerable<Uri> ExtractFeedLinks(string body, TrustedWebSource source)
    {
        foreach (Match match in RssLinkRegex.Matches(body))
        {
            var value = WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
            if (TryCreateAllowedUri(value, source, out var uri))
            {
                yield return uri;
            }
        }

        foreach (Match match in AtomLinkRegex.Matches(body))
        {
            var value = WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
            if (TryCreateAllowedUri(value, source, out var uri))
            {
                yield return uri;
            }
        }
    }

    private async Task<string?> FetchContentAsync(
        Uri uri,
        bool respectRobots,
        CancellationToken cancellationToken)
    {
        if (respectRobots && !await IsAllowedByRobotsAsync(uri, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var cacheKey = $"trusted-web:content:{uri.AbsoluteUri}";
        if (_cache.TryGetValue(cacheKey, out string? cached))
        {
            return cached;
        }

        var content = await FetchRawContentAsync(uri, cancellationToken).ConfigureAwait(false);
        if (content is not null && _options.CacheTtlSeconds > 0)
        {
            _cache.Set(cacheKey, content, TimeSpan.FromSeconds(_options.CacheTtlSeconds));
        }

        return content;
    }

    private async Task<string?> FetchRawContentAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("trusted-web");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyUserAgent(request);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || !IsAllowedContentType(response.Content.Headers.ContentType?.MediaType))
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return body.Length <= MaxRemoteChars ? body : body[..MaxRemoteChars];
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or InvalidOperationException) && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private void ApplyUserAgent(HttpRequestMessage request)
    {
        if (string.IsNullOrWhiteSpace(_options.UserAgent))
        {
            return;
        }

        if (ProductInfoHeaderValue.TryParse(_options.UserAgent, out var userAgent))
        {
            request.Headers.UserAgent.Add(userAgent);
            return;
        }

        request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
    }

    private async Task<bool> IsAllowedByRobotsAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!_options.RespectRobots)
        {
            return true;
        }

        var robotsUri = new Uri($"{uri.Scheme}://{uri.Authority}/robots.txt");
        var cacheKey = $"trusted-web:robots:{robotsUri.AbsoluteUri}";
        if (!_cache.TryGetValue(cacheKey, out string? robots))
        {
            robots = await FetchRawContentAsync(robotsUri, cancellationToken).ConfigureAwait(false);
            if (_options.CacheTtlSeconds > 0)
            {
                _cache.Set(cacheKey, robots ?? string.Empty, TimeSpan.FromSeconds(Math.Min(_options.CacheTtlSeconds, 3600)));
            }
        }

        if (string.IsNullOrWhiteSpace(robots))
        {
            return true;
        }

        var applies = false;
        foreach (var rawLine in robots.Split('\n'))
        {
            var line = rawLine.Split('#', 2)[0].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Equals("User-agent", StringComparison.OrdinalIgnoreCase))
            {
                applies = value == "*"
                    || _options.UserAgent.Contains(value, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (applies
                && key.Equals("Disallow", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(value)
                && uri.AbsolutePath.StartsWith(value, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private TrustedWebResult? BuildResult(
        TrustedWebSource source,
        Uri uri,
        string body,
        IReadOnlyList<string> queryTokens)
    {
        var text = ExtractPlainText(body);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var title = ExtractTitle(body);
        var score = Score(title, text, queryTokens);
        if (score <= 0)
        {
            return null;
        }

        var sourceId = ResolveSourceId(source, uri);
        var chunkId = BuildChunkId(uri);
        return new TrustedWebResult
        {
            SourceId = sourceId,
            SourceName = ResolveSourceName(source, sourceId),
            Url = uri.AbsoluteUri,
            CanonicalUrl = uri.AbsoluteUri,
            Title = string.IsNullOrWhiteSpace(title) ? uri.Host : title,
            Snippet = BuildSnippet(text, queryTokens, _options.MaxSnippetChars),
            ContentHash = TrustedWebEvidence.ComputeHash(text),
            SourceType = TrustedWebEvidence.SourceType,
            TrustTier = TrustedWebEvidence.TrustTier,
            RetrievedUtc = DateTimeOffset.UtcNow,
            ChunkId = chunkId,
            Score = score
        };
    }

    private static bool TryCreateAllowedUri(string? value, TrustedWebSource source, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var candidate)
            || (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        if (!IsAllowedDomain(candidate, source) || !IsAllowedPath(candidate, source))
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    private static bool IsAllowedDomain(Uri uri, TrustedWebSource source)
    {
        var allowedDomains = source.AllowedDomains
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Select(domain => domain.Trim().TrimStart('.'))
            .Concat(HostsFrom(source.SeedUrls))
            .Concat(HostsFrom(source.SitemapUrls))
            .Concat(HostsFrom(source.RssUrls))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return allowedDomains.Length > 0
            && allowedDomains.Any(domain =>
                uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith($".{domain}", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> HostsFrom(IEnumerable<string> urls)
    {
        foreach (var value in urls)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && !string.IsNullOrWhiteSpace(uri.Host))
            {
                yield return uri.Host;
            }
        }
    }

    private static bool IsAllowedPath(Uri uri, TrustedWebSource source)
    {
        if (source.IncludePathPrefixes.Count > 0
            && !source.IncludePathPrefixes.Any(prefix => uri.AbsolutePath.StartsWith(NormalizePathPrefix(prefix), StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !source.ExcludePathPrefixes.Any(prefix => uri.AbsolutePath.StartsWith(NormalizePathPrefix(prefix), StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePathPrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return "/";
        }

        var trimmed = prefix.Trim();
        return trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : $"/{trimmed}";
    }

    private static bool IsAllowedContentType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return true;
        }

        return mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
               || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase)
               || mediaType.Contains("text", StringComparison.OrdinalIgnoreCase)
               || mediaType.Contains("rss", StringComparison.OrdinalIgnoreCase)
               || mediaType.Contains("atom", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractTitle(string body)
    {
        var title = TitleRegex.Match(body);
        if (title.Success)
        {
            return NormalizeWhitespace(WebUtility.HtmlDecode(TagRegex.Replace(title.Groups[1].Value, " ")));
        }

        var h1 = H1Regex.Match(body);
        return h1.Success
            ? NormalizeWhitespace(WebUtility.HtmlDecode(TagRegex.Replace(h1.Groups[1].Value, " ")))
            : string.Empty;
    }

    private static string ExtractPlainText(string body)
    {
        var withoutScripts = ScriptStyleRegex.Replace(body, " ");
        var text = TagRegex.Replace(withoutScripts, " ");
        return NormalizeWhitespace(WebUtility.HtmlDecode(text));
    }

    private static string BuildSnippet(string text, IReadOnlyList<string> queryTokens, int maxSnippetChars)
    {
        if (text.Length <= maxSnippetChars)
        {
            return text;
        }

        var index = queryTokens
            .Select(token => text.IndexOf(token, StringComparison.OrdinalIgnoreCase))
            .Where(value => value >= 0)
            .DefaultIfEmpty(0)
            .Min();
        var start = Math.Max(0, index - 160);
        var length = Math.Min(maxSnippetChars, text.Length - start);
        return text.Substring(start, length).Trim();
    }

    private float Score(string title, string text, IReadOnlyList<string> queryTokens)
    {
        if (queryTokens.Count == 0)
        {
            return 0.1f;
        }

        var titleMatches = queryTokens.Count(token => title.Contains(token, StringComparison.OrdinalIgnoreCase));
        var textMatches = queryTokens.Count(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
        var distinctMatches = queryTokens.Count(token =>
            title.Contains(token, StringComparison.OrdinalIgnoreCase)
            || text.Contains(token, StringComparison.OrdinalIgnoreCase));
        var requiredMatches = queryTokens.Count >= 2 ? 2 : 1;
        if (distinctMatches < requiredMatches)
        {
            return 0;
        }

        var score = ((float)textMatches / queryTokens.Count) + ((float)titleMatches / queryTokens.Count * 0.2f);
        return Math.Min(1.0f, Math.Max(0.05f, score));
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        var token = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                token.Append(char.ToLowerInvariant(character));
                continue;
            }

            foreach (var emitted in EmitToken(token))
            {
                yield return emitted;
            }
        }

        foreach (var emitted in EmitToken(token))
        {
            yield return emitted;
        }
    }

    private static IEnumerable<string> EmitToken(StringBuilder token)
    {
        if (token.Length < 3)
        {
            token.Clear();
            yield break;
        }

        var value = token.ToString();
        token.Clear();
        if (!QueryStopWords.Contains(value))
        {
            yield return value;
        }
    }

    private string ResolveProfileName(TrustedWebContextRequest request, TrustedWebProfilesDocument? profiles = null)
    {
        if (!string.IsNullOrWhiteSpace(request.Profile))
        {
            return request.Profile.Trim();
        }

        if (!string.IsNullOrWhiteSpace(profiles?.DefaultProfile))
        {
            return profiles.DefaultProfile.Trim();
        }

        return string.IsNullOrWhiteSpace(_options.DefaultProfile) ? "general" : _options.DefaultProfile.Trim();
    }

    private int ClampResults(int value)
    {
        return Math.Min(_options.MaxResults, Math.Max(1, value));
    }

    private static string ResolveSourceId(TrustedWebSource source, Uri uri)
    {
        var value = source.SourceId ?? source.Id ?? uri.Host;
        return Slug(value);
    }

    private static string ResolveSourceName(TrustedWebSource source, string sourceId)
    {
        return string.IsNullOrWhiteSpace(source.DisplayName ?? source.Name)
            ? sourceId
            : (source.DisplayName ?? source.Name)!;
    }

    private static string BuildChunkId(Uri uri)
    {
        var path = uri.AbsolutePath.Trim('/').Replace('/', '-');
        var slug = Slug(string.IsNullOrWhiteSpace(path) ? "home" : path).Trim('-');
        return slug[..Math.Min(80, slug.Length)];
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        var slug = NormalizeHyphens(builder.ToString()).Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "source" : slug;
    }

    private static string NormalizeHyphens(string value)
    {
        while (value.Contains("--", StringComparison.Ordinal))
        {
            value = value.Replace("--", "-", StringComparison.Ordinal);
        }

        return value;
    }

    private static string NormalizeWhitespace(string value)
    {
        return string.Join(' ', value.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries));
    }
}
#nullable disable
