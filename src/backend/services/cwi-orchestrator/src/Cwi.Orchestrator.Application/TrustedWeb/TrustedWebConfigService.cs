#nullable enable
using System.Text.Json;
using System.Text.Json.Nodes;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.TrustedWeb;

public sealed class TrustedWebConfigService : ITrustedWebConfigStore
{
    public const string GlobalConfigTenantId = "__global_trusted_web__";

    private const string ConfigPropertyName = "trusted_web_context";
    private readonly ITenantConfigRepository _tenantConfigRepository;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public TrustedWebConfigService(ITenantConfigRepository tenantConfigRepository)
    {
        _tenantConfigRepository = tenantConfigRepository;
    }

    public async Task<ServiceResponse<TrustedWebConfigDto>> GetAsync(
        string? tenantId,
        CancellationToken cancellationToken)
    {
        var dto = await ReadGlobalConfigAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return ServiceResponse<TrustedWebConfigDto>.Success(dto ?? CreateEmptyConfig(source: "database_empty"));
    }

    public async Task<ServiceResponse<TrustedWebConfigDto>> SaveAsync(
        string? tenantId,
        TrustedWebConfigDto request,
        string? actorUserId,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(request, actorUserId);
        var validationError = Validate(normalized);
        if (validationError is not null)
        {
            return Fail(validationError);
        }

        var existing = await _tenantConfigRepository.GetAsync(GlobalConfigTenantId, cancellationToken)
            .ConfigureAwait(false);
        var root = ParseRoot(existing?.ConfigJson);
        root[ConfigPropertyName] = JsonSerializer.SerializeToNode(normalized, _jsonOptions);

        var now = DateTimeOffset.UtcNow;
        var record = existing ?? new TenantConfig
        {
            Id = Guid.NewGuid(),
            TenantId = GlobalConfigTenantId,
            CreatedUtc = now
        };
        record.ConfigJson = root.ToJsonString(_jsonOptions);
        record.UpdatedUtc = now;

        await _tenantConfigRepository.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        return ServiceResponse<TrustedWebConfigDto>.Success(normalized);
    }

    public async Task<TrustedWebRuntimeConfig> LoadRuntimeConfigAsync(
        string? tenantId,
        CancellationToken cancellationToken)
    {
        var config = await ReadGlobalConfigAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (config is null)
        {
            return new TrustedWebRuntimeConfig(null);
        }

        if (!config.Enabled)
        {
            return new TrustedWebRuntimeConfig(null, "trusted_web_profile_disabled", DatabaseConfigured: true);
        }

        var document = ToProfilesDocument(config);
        return new TrustedWebRuntimeConfig(document, DatabaseConfigured: true);
    }

    private async Task<TrustedWebConfigDto?> ReadGlobalConfigAsync(
        string? legacyTenantId,
        CancellationToken cancellationToken)
    {
        var globalConfig = await ReadConfigAsync(GlobalConfigTenantId, cancellationToken).ConfigureAwait(false);
        if (globalConfig is not null)
        {
            return globalConfig;
        }

        var fallbackTenantId = ResolveLegacyTenantId(legacyTenantId);
        return string.IsNullOrWhiteSpace(fallbackTenantId)
            ? null
            : await ReadConfigAsync(fallbackTenantId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TrustedWebConfigDto?> ReadConfigAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var record = await _tenantConfigRepository.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (record is null || string.IsNullOrWhiteSpace(record.ConfigJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(record.ConfigJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(ConfigPropertyName, out var trustedWebElement) ||
                trustedWebElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var config = trustedWebElement.Deserialize<TrustedWebConfigDto>(_jsonOptions);
            return config is null
                ? null
                : Normalize(config, config.UpdatedBy, config.Source ?? "database", config.UpdatedUtc);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TrustedWebConfigDto CreateEmptyConfig(string source)
    {
        return new TrustedWebConfigDto
        {
            Enabled = false,
            Version = 1,
            DefaultProfile = "general",
            Source = source,
            Profiles = new Dictionary<string, TrustedWebProfileConfigDto>(StringComparer.OrdinalIgnoreCase)
            {
                ["general"] = new()
                {
                    Mode = "trusted_sites_only",
                    MaxResults = 5,
                    Sources = Array.Empty<TrustedWebSourceConfigDto>()
                }
            }
        };
    }

    private static TrustedWebConfigDto Normalize(
        TrustedWebConfigDto request,
        string? actorUserId,
        string source = "database",
        string? updatedUtc = null)
    {
        var defaultProfile = NormalizeProfileName(request.DefaultProfile);
        var profiles = new Dictionary<string, TrustedWebProfileConfigDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in request.Profiles ?? new Dictionary<string, TrustedWebProfileConfigDto>())
        {
            var name = NormalizeProfileName(pair.Key);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var profile = pair.Value ?? new TrustedWebProfileConfigDto();
            profiles[name] = new TrustedWebProfileConfigDto
            {
                Description = TrimToNull(profile.Description),
                Mode = string.IsNullOrWhiteSpace(profile.Mode) ? "trusted_sites_only" : profile.Mode.Trim(),
                MaxResults = profile.MaxResults is null ? null : Math.Clamp(profile.MaxResults.Value, 1, 10),
                Sources = (profile.Sources ?? Array.Empty<TrustedWebSourceConfigDto>())
                    .Select(NormalizeSource)
                    .Where(source => source is not null)
                    .Cast<TrustedWebSourceConfigDto>()
                    .ToArray()
            };
        }

        if (profiles.Count == 0)
        {
            profiles[defaultProfile] = new TrustedWebProfileConfigDto();
        }

        if (!profiles.ContainsKey(defaultProfile))
        {
            defaultProfile = profiles.Keys.First();
        }

        return new TrustedWebConfigDto
        {
            Enabled = request.Enabled,
            Version = request.Version <= 0 ? 1 : request.Version,
            DefaultProfile = defaultProfile,
            Profiles = profiles,
            Source = source,
            UpdatedUtc = updatedUtc ?? DateTimeOffset.UtcNow.ToString("O"),
            UpdatedBy = TrimToNull(actorUserId)
        };
    }

    private static TrustedWebSourceConfigDto? NormalizeSource(TrustedWebSourceConfigDto source)
    {
        var sourceId = TrimToNull(source.SourceId);
        var displayName = TrimToNull(source.DisplayName);
        var allowedDomains = NormalizeDomains(source.AllowedDomains);
        var seedUrls = NormalizeUrls(source.SeedUrls);
        var sitemapUrls = NormalizeUrls(source.SitemapUrls);
        var rssUrls = NormalizeUrls(source.RssUrls);
        var includePaths = NormalizePaths(source.IncludePathPrefixes);
        var excludePaths = NormalizePaths(source.ExcludePathPrefixes);

        if (string.IsNullOrWhiteSpace(sourceId)
            && string.IsNullOrWhiteSpace(displayName)
            && allowedDomains.Count == 0
            && seedUrls.Count == 0
            && sitemapUrls.Count == 0
            && rssUrls.Count == 0)
        {
            return null;
        }

        return new TrustedWebSourceConfigDto
        {
            SourceId = sourceId,
            DisplayName = displayName,
            AllowedDomains = allowedDomains,
            SeedUrls = seedUrls,
            SitemapUrls = sitemapUrls,
            RssUrls = rssUrls,
            IncludePathPrefixes = includePaths,
            ExcludePathPrefixes = excludePaths
        };
    }

    private static string? Validate(TrustedWebConfigDto config)
    {
        if (string.IsNullOrWhiteSpace(config.DefaultProfile))
        {
            return "default_profile required";
        }

        foreach (var pair in config.Profiles)
        {
            if (!string.Equals(pair.Value.Mode, "trusted_sites_only", StringComparison.OrdinalIgnoreCase))
            {
                return $"profile '{pair.Key}' mode must be trusted_sites_only";
            }

            foreach (var source in pair.Value.Sources ?? Array.Empty<TrustedWebSourceConfigDto>())
            {
                if (source.AllowedDomains.Count == 0
                    && source.SeedUrls.Count == 0
                    && source.SitemapUrls.Count == 0
                    && source.RssUrls.Count == 0)
                {
                    return $"profile '{pair.Key}' has a source without trusted domains or URLs";
                }

                var allUrls = source.SeedUrls.Concat(source.SitemapUrls).Concat(source.RssUrls);
                foreach (var url in allUrls)
                {
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                        || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                    {
                        return $"invalid trusted URL '{url}'";
                    }
                }
            }
        }

        return null;
    }

    private static TrustedWebProfilesDocument ToProfilesDocument(TrustedWebConfigDto config)
    {
        return new TrustedWebProfilesDocument
        {
            Version = config.Version <= 0 ? 1 : config.Version,
            DefaultProfile = config.DefaultProfile,
            Profiles = config.Profiles.ToDictionary(
                pair => pair.Key,
                pair => new TrustedWebProfile
                {
                    Description = pair.Value.Description,
                    Mode = pair.Value.Mode,
                    MaxResults = pair.Value.MaxResults,
                    Sources = pair.Value.Sources.Select(source => new TrustedWebSource
                    {
                        SourceId = source.SourceId,
                        DisplayName = source.DisplayName,
                        AllowedDomains = source.AllowedDomains,
                        SeedUrls = source.SeedUrls,
                        SitemapUrls = source.SitemapUrls,
                        RssUrls = source.RssUrls,
                        IncludePathPrefixes = source.IncludePathPrefixes,
                        ExcludePathPrefixes = source.ExcludePathPrefixes
                    }).ToArray()
                },
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private static JsonObject ParseRoot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static List<string> NormalizeDomains(IEnumerable<string> values)
    {
        return values
            .Select(value => NormalizeDomain(value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizeDomain(string? value)
    {
        var trimmed = TrimToNull(value);
        if (trimmed is null)
        {
            return null;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            trimmed = uri.Host;
        }

        trimmed = trimmed.Trim().TrimStart('.').ToLowerInvariant();
        return trimmed.Length == 0 || trimmed.Contains('/') ? null : trimmed;
    }

    private static List<string> NormalizeUrls(IEnumerable<string> values)
    {
        return values
            .Select(value => TrimToNull(value))
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> NormalizePaths(IEnumerable<string> values)
    {
        return values
            .Select(NormalizePath)
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizePath(string? value)
    {
        var trimmed = TrimToNull(value);
        if (trimmed is null)
        {
            return null;
        }

        return trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : $"/{trimmed}";
    }

    private static string? ResolveLegacyTenantId(string? tenantId)
    {
        var envTenant = TrimToNull(Environment.GetEnvironmentVariable("DEFAULT_TENANT_ID"));
        if (envTenant is not null)
        {
            return envTenant;
        }

        return TrimToNull(tenantId);
    }

    private static string NormalizeProfileName(string? value)
    {
        var trimmed = TrimToNull(value) ?? "general";
        return trimmed.Length > 64 ? trimmed[..64] : trimmed;
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static ServiceResponse<TrustedWebConfigDto> Fail(string message, int statusCode = 400)
    {
        return ServiceResponse<TrustedWebConfigDto>.Fail(
            new DomainError(ErrorCodes.InvalidRequest, message),
            statusCode: statusCode,
            message: message);
    }
}
#nullable disable
