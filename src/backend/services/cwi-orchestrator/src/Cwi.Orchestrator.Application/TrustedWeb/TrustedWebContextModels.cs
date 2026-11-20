#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.TrustedWeb;

public sealed record TrustedWebContextRequest(string Query, string? Profile, int? MaxResults, string? TenantId = null);

public sealed record TrustedWebContextResult(
    IReadOnlyList<TrustedWebResult> Results,
    string Profile,
    string? SkippedReason = null);

public sealed class TrustedWebResult
{
    public string SourceId { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string CanonicalUrl { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Snippet { get; init; } = string.Empty;
    public string ContentHash { get; init; } = string.Empty;
    public string SourceType { get; init; } = TrustedWebEvidence.SourceType;
    public string TrustTier { get; init; } = TrustedWebEvidence.TrustTier;
    public DateTimeOffset? PublishedUtc { get; init; }
    public DateTimeOffset RetrievedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string ChunkId { get; init; } = string.Empty;
    public float Score { get; init; }
}

public interface ITrustedWebContextProvider
{
    Task<TrustedWebContextResult> RetrieveAsync(
        TrustedWebContextRequest request,
        CancellationToken cancellationToken);
}

public sealed record TrustedWebRuntimeConfig(
    TrustedWebProfilesDocument? Profiles,
    string? SkippedReason = null,
    bool DatabaseConfigured = false);

public interface ITrustedWebConfigStore
{
    Task<TrustedWebRuntimeConfig> LoadRuntimeConfigAsync(
        string? tenantId,
        CancellationToken cancellationToken);
}

public sealed class NullTrustedWebConfigStore : ITrustedWebConfigStore
{
    public static NullTrustedWebConfigStore Instance { get; } = new();

    private NullTrustedWebConfigStore()
    {
    }

    public Task<TrustedWebRuntimeConfig> LoadRuntimeConfigAsync(
        string? tenantId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new TrustedWebRuntimeConfig(null));
    }
}

public sealed class TrustedWebProfilesDocument
{
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("default_profile")]
    public string? DefaultProfile { get; init; }

    [JsonPropertyName("profiles")]
    public Dictionary<string, TrustedWebProfile> Profiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TrustedWebProfile
{
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "trusted_sites_only";

    [JsonPropertyName("max_results")]
    public int? MaxResults { get; init; }

    [JsonPropertyName("sources")]
    public IReadOnlyList<TrustedWebSource> Sources { get; init; } = Array.Empty<TrustedWebSource>();
}

public sealed class TrustedWebSource
{
    [JsonPropertyName("source_id")]
    public string? SourceId { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("allowed_domains")]
    public IReadOnlyList<string> AllowedDomains { get; init; } = Array.Empty<string>();

    [JsonPropertyName("seed_urls")]
    public IReadOnlyList<string> SeedUrls { get; init; } = Array.Empty<string>();

    [JsonPropertyName("sitemap_urls")]
    public IReadOnlyList<string> SitemapUrls { get; init; } = Array.Empty<string>();

    [JsonPropertyName("rss_urls")]
    public IReadOnlyList<string> RssUrls { get; init; } = Array.Empty<string>();

    [JsonPropertyName("include_path_prefixes")]
    public IReadOnlyList<string> IncludePathPrefixes { get; init; } = Array.Empty<string>();

    [JsonPropertyName("exclude_path_prefixes")]
    public IReadOnlyList<string> ExcludePathPrefixes { get; init; } = Array.Empty<string>();
}
#nullable disable
