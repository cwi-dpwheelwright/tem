using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class TrustedWebConfigDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("default_profile")]
    public string DefaultProfile { get; init; } = "general";

    [JsonPropertyName("profiles")]
    public IReadOnlyDictionary<string, TrustedWebProfileConfigDto> Profiles { get; init; } =
        new Dictionary<string, TrustedWebProfileConfigDto>(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }

    [JsonPropertyName("updated_utc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UpdatedUtc { get; init; }

    [JsonPropertyName("updated_by")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UpdatedBy { get; init; }
}

public sealed class TrustedWebProfileConfigDto
{
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "trusted_sites_only";

    [JsonPropertyName("max_results")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxResults { get; init; }

    [JsonPropertyName("sources")]
    public IReadOnlyList<TrustedWebSourceConfigDto> Sources { get; init; } = Array.Empty<TrustedWebSourceConfigDto>();
}

public sealed class TrustedWebSourceConfigDto
{
    [JsonPropertyName("source_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceId { get; init; }

    [JsonPropertyName("display_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; init; }

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
