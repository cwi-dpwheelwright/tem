using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Journey;
using Microsoft.Extensions.Logging;

namespace Cwi.Orchestrator.Application.Packs;

public sealed class PackFetcher
{
    private readonly ApplicationOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<PackFetcher> _logger;
    private readonly JourneyRequestContext _journeyContext;

    public PackFetcher(
        ApplicationOptions options,
        IHttpClientFactory httpClientFactory,
        JourneyRequestContext journeyContext,
        ILogger<PackFetcher> logger)
    {
        _options = options;
        _httpClient = httpClientFactory.CreateClient("packs");
        _journeyContext = journeyContext;
        _logger = logger;
    }

    public async Task<ServiceResponse<PackDocument>> FetchAsync(string packName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packName))
        {
            return ServiceResponse<PackDocument>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Pack name is required."),
                statusCode: 400,
                message: "Pack name required");
        }

        var baseUrl = _options.PacksBaseUrl.TrimEnd('/');
        var packUrl = $"{baseUrl}/packs/{packName}/pack.json";
        var anchorUrl = $"{baseUrl}/packs/{packName}/anchor.txt";

        try
        {
            _logger.LogInformation(
                "Fetching legacy pack {PackName}. PackUrl {PackUrl} AnchorUrl {AnchorUrl}.",
                packName,
                packUrl,
                anchorUrl);
            using var packRequest = BuildRequest(packUrl, packName);
            using var packResponse = await _httpClient.SendAsync(packRequest, cancellationToken).ConfigureAwait(false);
            if (!packResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Pack fetch failed for {PackName} with status {StatusCode}.", packName, packResponse.StatusCode);
                return ServiceResponse<PackDocument>.Fail(
                    new DomainError(ErrorCodes.DependencyUnavailable, "Pack fetch failed."),
                    statusCode: (int)packResponse.StatusCode,
                    message: "Pack fetch failed");
            }

            var packJson = await packResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            using var anchorRequest = BuildRequest(anchorUrl, packName);
            using var anchorResponse = await _httpClient.SendAsync(anchorRequest, cancellationToken).ConfigureAwait(false);
            if (!anchorResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Pack anchor fetch failed for {PackName} with status {StatusCode}.", packName, anchorResponse.StatusCode);
                return ServiceResponse<PackDocument>.Fail(
                    new DomainError(ErrorCodes.DependencyUnavailable, "Pack anchor fetch failed."),
                    statusCode: (int)anchorResponse.StatusCode,
                    message: "Pack anchor fetch failed");
            }

            var anchor = (await anchorResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();

            using var document = JsonDocument.Parse(packJson);
            var root = document.RootElement;
            var version = TryGetPackVersion(root);
            if (string.IsNullOrWhiteSpace(version))
            {
                _logger.LogWarning("Pack version missing for {PackName}.", packName);
                return ServiceResponse<PackDocument>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "Pack version missing."),
                    statusCode: 400,
                    message: "Pack version missing");
            }

            var canonicalHash = PackAnchor.ComputeHash(root);
            var matchesCanonical = string.Equals(canonicalHash, anchor, StringComparison.OrdinalIgnoreCase);
            if (!matchesCanonical)
            {
                _logger.LogWarning(
                    "Pack anchor mismatch for {PackName}. Canonical {CanonicalHash} vs anchor {Anchor}.",
                    packName,
                    canonicalHash,
                    anchor);
                return ServiceResponse<PackDocument>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "Pack anchor mismatch."),
                    statusCode: 400,
                    message: "Pack verification failed");
            }

            var resolvedHash = canonicalHash;
            var metadata = PackManifestReader.Read(root);
            var resolvedName = metadata.DeclaredName?.Trim();
            if (string.IsNullOrWhiteSpace(resolvedName))
            {
                resolvedName = packName;
            }
            else if (!string.Equals(resolvedName, packName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Legacy pack name mapped from {Requested} to {Resolved}.",
                    packName,
                    resolvedName);
            }

            var descriptor = new PackDescriptor(
                packName,
                resolvedName,
                version,
                resolvedHash,
                anchor,
                PackContractType.Legacy,
                new[] { packUrl, anchorUrl },
                metadata.Entries,
                metadata.Modules,
                packTarget: resolvedName);

            var result = new PackDocument(resolvedName, version, packJson, anchor, resolvedHash, PackContractType.Legacy, descriptor);
            _logger.LogInformation(
                "Legacy pack resolved {PackName} version {Version} hash {Hash}.",
                resolvedName,
                version,
                resolvedHash);
            return ServiceResponse<PackDocument>.Success(result);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Pack fetch failed for {PackName} due to HTTP error.", packName);
            return ServiceResponse<PackDocument>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Pack fetch failed."),
                statusCode: 503,
                message: "Pack fetch failed");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Pack fetch timed out for {PackName}.", packName);
            return ServiceResponse<PackDocument>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Pack fetch timed out."),
                statusCode: 504,
                message: "Pack fetch timed out");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Pack manifest invalid JSON for {PackName}.", packName);
            return ServiceResponse<PackDocument>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Pack manifest invalid."),
                statusCode: 400,
                message: "Pack manifest invalid");
        }
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

    private static string? TryGetPackVersion(JsonElement root)
    {
        var version = TryGetString(root, "pack_version") ?? TryGetString(root, "version");
        return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }
}
