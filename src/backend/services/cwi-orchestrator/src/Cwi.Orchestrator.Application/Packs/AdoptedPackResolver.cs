using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Journey;
using Microsoft.Extensions.Logging;

namespace Cwi.Orchestrator.Application.Packs;

public sealed class AdoptedPackResolver : IPackContractResolver
{
    private static readonly IReadOnlySet<string> AnchorExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "anchor",
        "anchor_hash",
        "pack_hash"
    };

    private readonly ApplicationOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AdoptedPackResolver> _logger;
    private readonly IPackSignatureValidator _signatureValidator;
    private readonly JourneyRequestContext _journeyContext;

    public AdoptedPackResolver(
        ApplicationOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<AdoptedPackResolver> logger,
        IPackSignatureValidator signatureValidator,
        JourneyRequestContext journeyContext)
    {
        _options = options;
        _httpClient = httpClientFactory.CreateClient("packs");
        _logger = logger;
        _signatureValidator = signatureValidator;
        _journeyContext = journeyContext;
    }

    public PackContractType ContractType => PackContractType.Adopted;

    public async Task<ServiceResponse<PackDocument>> ResolveAsync(string packName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packName))
        {
            return ServiceResponse<PackDocument>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Pack name is required."),
                statusCode: 400,
                message: "Pack name required");
        }

        var manifestUri = BuildManifestUri();
        try
        {
            _logger.LogInformation(
                "Resolving adopted pack {PackName} using manifest {ManifestUri}.",
                packName,
                manifestUri);
            using var manifestRequest = BuildRequest(manifestUri.ToString(), packName);
            using var manifestResponse = await _httpClient.SendAsync(manifestRequest, cancellationToken).ConfigureAwait(false);
            if (!manifestResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Adopted manifest fetch failed for {PackName} with status {StatusCode}.",
                    packName,
                    manifestResponse.StatusCode);
                return ServiceResponse<PackDocument>.Fail(
                    new DomainError(ErrorCodes.DependencyUnavailable, "Adopted manifest unavailable."),
                    statusCode: (int)manifestResponse.StatusCode,
                    message: "Adopted manifest unavailable");
            }

            var manifestJson = await manifestResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            JsonDocument manifestDoc;
            try
            {
                manifestDoc = JsonDocument.Parse(manifestJson);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Adopted manifest JSON invalid for {PackName}.", packName);
                return ServiceResponse<PackDocument>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "Adopted manifest invalid."),
                    statusCode: 400,
                    message: "Adopted manifest invalid");
            }

            using var manifestScope = manifestDoc;
            var record = FindRecord(manifestDoc.RootElement, packName);
            if (record is null)
            {
                _logger.LogInformation("Pack {PackName} not found in adopted manifest.", packName);
                return ServiceResponse<PackDocument>.Fail(
                    new DomainError(ErrorCodes.DependencyUnavailable, "Adopted pack not found."),
                    statusCode: 404,
                    message: "Adopted pack not found");
            }

            _logger.LogInformation(
                "Adopted manifest entry found. PackTarget {PackTarget} PublicationSlug {Slug} PublicationVersion {Version} Anchor {Anchor}.",
                record.PackTarget ?? "n/a",
                record.PublicationSlug ?? "n/a",
                record.PublicationVersion ?? "n/a",
                record.AnchorHash ?? "n/a");

            var artifactLocations = new List<string> { manifestUri.ToString() };
            var packJson = record.EntryJson;
            string? packSource = null;
            var packFile = record.PublishedFile ?? record.LatestFile;
            if (!string.IsNullOrWhiteSpace(packFile))
            {
                var packUri = BuildPackUri(manifestUri, packFile!);
                packSource = packUri.ToString();
                artifactLocations.Add(packSource);

                using var packRequest = BuildRequest(packUri.ToString(), packName);
                using var packResponse = await _httpClient.SendAsync(packRequest, cancellationToken).ConfigureAwait(false);
                if (!packResponse.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Adopted pack file fetch failed for {PackName} with status {StatusCode}.",
                        packName,
                        packResponse.StatusCode);
                    return ServiceResponse<PackDocument>.Fail(
                        new DomainError(ErrorCodes.DependencyUnavailable, "Adopted pack file unavailable."),
                        statusCode: (int)packResponse.StatusCode,
                        message: "Adopted pack file unavailable");
                }

                packJson = await packResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(packJson))
            {
                _logger.LogWarning("Adopted pack {PackName} missing pack file reference.", packName);
                return ServiceResponse<PackDocument>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "Adopted pack file missing."),
                    statusCode: 400,
                    message: "Adopted pack file missing");
            }

            JsonDocument packDoc;
            try
            {
                packDoc = JsonDocument.Parse(packJson);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Adopted pack JSON invalid for {PackName}.", packName);
                return ServiceResponse<PackDocument>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "Adopted pack manifest invalid."),
                    statusCode: 400,
                    message: "Adopted pack manifest invalid");
            }

            using var packScope = packDoc;
            var root = packDoc.RootElement;
            var version = ResolveVersion(root, record);
            if (string.IsNullOrWhiteSpace(version))
            {
                _logger.LogWarning("Adopted pack {PackName} missing publication version.", packName);
                return ServiceResponse<PackDocument>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "Adopted pack version missing."),
                    statusCode: 400,
                    message: "Adopted pack version missing");
            }

            var anchor = ResolveAnchor(root, record);
            if (string.IsNullOrWhiteSpace(anchor))
            {
                _logger.LogWarning("Adopted pack {PackName} missing anchor hash.", packName);
                return ServiceResponse<PackDocument>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "Adopted pack anchor missing."),
                    statusCode: 400,
                    message: "Adopted pack anchor missing");
            }

            var computed = PackAnchor.ComputeHash(root);
            if (!string.Equals(computed, anchor, StringComparison.OrdinalIgnoreCase))
            {
                if (HasAnchorField(root))
                {
                    var strippedComputed = PackAnchor.ComputeHash(root, AnchorExclusions);
                    if (string.Equals(strippedComputed, anchor, StringComparison.OrdinalIgnoreCase))
                    {
                        computed = strippedComputed;
                        _logger.LogInformation(
                            "Adopted pack {PackName} anchor verified after excluding anchor fields from canonicalization.",
                            packName);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Adopted pack anchor mismatch for {PackName}. Computed {Computed} vs anchor {Anchor}.",
                            packName,
                            computed,
                            anchor);
                        return ServiceResponse<PackDocument>.Fail(
                            new DomainError(ErrorCodes.InvalidRequest, "Adopted pack anchor mismatch."),
                            statusCode: 400,
                            message: "Adopted pack verification failed");
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Adopted pack anchor mismatch for {PackName}. Computed {Computed} vs anchor {Anchor}.",
                        packName,
                        computed,
                        anchor);
                    return ServiceResponse<PackDocument>.Fail(
                        new DomainError(ErrorCodes.InvalidRequest, "Adopted pack anchor mismatch."),
                        statusCode: 400,
                        message: "Adopted pack verification failed");
                }
            }

            var metadata = PackManifestReader.Read(root);
            var resolvedName = metadata.DeclaredName
                ?? record.PublicationSlug
                ?? record.PackTarget
                ?? packName;

            if (!string.Equals(packName, resolvedName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Adopted pack name mapped from {Requested} to {Resolved}.",
                    packName,
                    resolvedName);
            }

            var descriptor = new PackDescriptor(
                packName,
                resolvedName,
                version,
                computed,
                anchor,
                PackContractType.Adopted,
                artifactLocations,
                metadata.Entries,
                metadata.Modules,
                packTarget: record.PackTarget,
                publicationSlug: record.PublicationSlug,
                publicationVersion: record.PublicationVersion ?? version,
                publicationPublishedFile: record.PublishedFile,
                publicationLatestFile: record.LatestFile);

            var document = new PackDocument(resolvedName, version, packJson, anchor, computed, PackContractType.Adopted, descriptor);
            var signature = _signatureValidator.Validate(document);
            if (!signature.IsSuccess)
            {
                return ServiceResponse<PackDocument>.Fail(
                    signature.Error ?? new DomainError(ErrorCodes.InvalidRequest, "Pack signature invalid."),
                    statusCode: signature.StatusCode,
                    message: signature.Message);
            }

            _logger.LogInformation(
                "Adopted pack resolved for {PackName}. Target {PackTarget} Version {Version} Source {Source}.",
                packName,
                record.PackTarget ?? record.PublicationSlug ?? resolvedName,
                version,
                packSource ?? manifestUri.ToString());

            return ServiceResponse<PackDocument>.Success(document);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Adopted pack fetch failed for {PackName}.", packName);
            return ServiceResponse<PackDocument>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Adopted pack fetch failed."),
                statusCode: 503,
                message: "Adopted pack fetch failed");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Adopted pack fetch timed out for {PackName}.", packName);
            return ServiceResponse<PackDocument>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Adopted pack fetch timed out."),
                statusCode: 504,
                message: "Adopted pack fetch timed out");
        }
    }

    private Uri BuildManifestUri()
    {
        if (Uri.TryCreate(_options.PacksAdoptedManifestPath, UriKind.Absolute, out var absolute)
            && (string.Equals(absolute.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(absolute.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return absolute;
        }

        var baseUrl = string.IsNullOrWhiteSpace(_options.PacksAdoptedBaseUrl)
            ? _options.PacksBaseUrl
            : _options.PacksAdoptedBaseUrl!;

        var baseUri = new Uri(baseUrl.EndsWith("/") ? baseUrl : $"{baseUrl}/");
        var path = _options.PacksAdoptedManifestPath.TrimStart('/');
        return new Uri(baseUri, path);
    }

    private static Uri BuildPackUri(Uri manifestUri, string packFile)
    {
        if (Uri.TryCreate(packFile, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        var manifestBase = new Uri(manifestUri, "./");
        return new Uri(manifestBase, packFile.TrimStart('/'));
    }

    private HttpRequestMessage BuildRequest(string url, string packName)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(_journeyContext.RequestId))
        {
            request.Headers.TryAddWithoutValidation("x-request-id", _journeyContext.RequestId);
        }
        if (!string.IsNullOrWhiteSpace(_journeyContext.TraceId))
        {
            request.Headers.TryAddWithoutValidation("x-trace-id", _journeyContext.TraceId);
        }
        if (!string.IsNullOrWhiteSpace(_journeyContext.TenantId))
        {
            request.Headers.TryAddWithoutValidation("x-tenant-id", _journeyContext.TenantId);
        }
        if (!string.IsNullOrWhiteSpace(_journeyContext.UserId))
        {
            request.Headers.TryAddWithoutValidation("x-user-id", _journeyContext.UserId);
        }

        request.Headers.TryAddWithoutValidation("x-pack-name", packName);
        return request;
    }

    private static AdoptedPackRecord? FindRecord(JsonElement root, string packName)
    {
        foreach (var entry in EnumerateEntries(root))
        {
            if (!TryParseEntry(entry, out var record))
            {
                continue;
            }

            if (MatchesPackName(record, packName))
            {
                return record;
            }
        }

        return null;
    }

    private static IEnumerable<JsonElement> EnumerateEntries(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    yield return item;
                }
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (root.TryGetProperty("packs", out var packs) && packs.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in packs.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    yield return item;
                }
            }

            yield break;
        }

        if (root.TryGetProperty("adoptions", out var adoptions) && adoptions.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in adoptions.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    yield return item;
                }
            }

            yield break;
        }

        if (root.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in entries.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    yield return item;
                }
            }

            yield break;
        }

        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (LooksLikeEntry(prop.Value))
            {
                yield return prop.Value;
            }
        }
    }

    private static bool LooksLikeEntry(JsonElement element)
    {
        return element.TryGetProperty("pack_target", out _)
               || element.TryGetProperty("publication", out _)
               || element.TryGetProperty("anchor_hash", out _)
               || element.TryGetProperty("pack_id", out _);
    }

    private static bool TryParseEntry(JsonElement element, out AdoptedPackRecord record)
    {
        var packTarget = TryGetString(element, "pack_target")
            ?? TryGetString(element, "target")
            ?? TryGetString(element, "pack")
            ?? TryGetString(element, "pack_id");

        var anchorHash = TryGetString(element, "anchor_hash")
            ?? TryGetString(element, "anchor")
            ?? TryGetString(element, "pack_hash");

        string? publicationSlug = null;
        string? publicationVersion = null;
        string? publishedFile = null;
        string? latestFile = null;
        if (element.TryGetProperty("publication", out var publication) && publication.ValueKind == JsonValueKind.Object)
        {
            publicationSlug = TryGetString(publication, "slug")
                ?? TryGetString(publication, "name")
                ?? TryGetString(publication, "pack")
                ?? TryGetString(publication, "pack_id");
            publicationVersion = TryGetString(publication, "version");
            publishedFile = TryGetString(publication, "published_file")
                ?? TryGetString(publication, "file");
            latestFile = TryGetString(publication, "latest_file");
        }

        publicationSlug ??= TryGetString(element, "publication_slug") ?? TryGetString(element, "slug");
        publicationVersion ??= TryGetString(element, "publication_version") ?? TryGetString(element, "version");
        publishedFile ??= TryGetString(element, "published_file") ?? TryGetString(element, "file");
        latestFile ??= TryGetString(element, "latest_file");

        record = new AdoptedPackRecord(
            packTarget,
            anchorHash,
            publicationSlug,
            publicationVersion,
            publishedFile,
            latestFile,
            element.GetRawText());

        return !string.IsNullOrWhiteSpace(record.PackTarget) || !string.IsNullOrWhiteSpace(record.PublicationSlug);
    }

    private static bool MatchesPackName(AdoptedPackRecord record, string packName)
    {
        return IsNameMatch(packName, record.PackTarget)
               || IsNameMatch(packName, record.PublicationSlug);
    }

    private static bool IsNameMatch(string requested, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        return string.Equals(requested.Trim(), candidate.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveVersion(JsonElement root, AdoptedPackRecord record)
    {
        var version = record.PublicationVersion
            ?? TryGetNestedString(root, "publication", "version")
            ?? TryGetString(root, "pack_version")
            ?? TryGetString(root, "version");

        return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
    }

    private static string? ResolveAnchor(JsonElement root, AdoptedPackRecord record)
    {
        var anchor = record.AnchorHash
            ?? TryGetString(root, "anchor_hash")
            ?? TryGetString(root, "anchor")
            ?? TryGetString(root, "pack_hash");

        return string.IsNullOrWhiteSpace(anchor) ? null : anchor.Trim();
    }

    private static bool HasAnchorField(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object &&
               (root.TryGetProperty("anchor_hash", out _) ||
                root.TryGetProperty("anchor", out _) ||
                root.TryGetProperty("pack_hash", out _));
    }

    private static string? TryGetNestedString(JsonElement root, string objectName, string propertyName)
    {
        if (!root.TryGetProperty(objectName, out var container) || container.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryGetString(container, propertyName);
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private sealed record AdoptedPackRecord(
        string? PackTarget,
        string? AnchorHash,
        string? PublicationSlug,
        string? PublicationVersion,
        string? PublishedFile,
        string? LatestFile,
        string? EntryJson);
}
