using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Journey;
using Microsoft.Extensions.Logging;

namespace Cwi.Orchestrator.Application.Packs;

public sealed class BaselinePackContentProvider
{
    private readonly ApplicationOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ICorePackRuntimeCatalog _corePackRuntimeCatalog;
    private readonly JourneyRequestContext _journeyContext;
    private readonly ILogger<BaselinePackContentProvider> _logger;

    public BaselinePackContentProvider(
        ApplicationOptions options,
        IHttpClientFactory httpClientFactory,
        ICorePackRuntimeCatalog corePackRuntimeCatalog,
        JourneyRequestContext journeyContext,
        ILogger<BaselinePackContentProvider> logger)
    {
        _options = options;
        _httpClient = httpClientFactory.CreateClient("packs");
        _corePackRuntimeCatalog = corePackRuntimeCatalog;
        _journeyContext = journeyContext;
        _logger = logger;
    }

    public async Task<ServiceResponse<IReadOnlyList<BaselinePackGuidance>>> FetchAsync(
        IReadOnlyList<PackDocument> baselinePacks,
        CancellationToken cancellationToken)
    {
        if (baselinePacks.Count == 0)
        {
            return ServiceResponse<IReadOnlyList<BaselinePackGuidance>>.Success(Array.Empty<BaselinePackGuidance>());
        }

        var guidance = new List<BaselinePackGuidance>();
        foreach (var pack in baselinePacks)
        {
            if (_corePackRuntimeCatalog.IsManaged(pack.Name)
                && _corePackRuntimeCatalog.TryGetCurrent(pack.Name, out var runtimeEntry)
                && runtimeEntry is not null
                && string.Equals(runtimeEntry.Document.Hash, pack.Hash, StringComparison.OrdinalIgnoreCase))
            {
                guidance.Add(runtimeEntry.Guidance);
                continue;
            }

            if (_corePackRuntimeCatalog.IsManaged(pack.Name))
            {
                var runtimeResult = await _corePackRuntimeCatalog.GetAsync(pack.Name, cancellationToken).ConfigureAwait(false);
                if (runtimeResult.IsSuccess && runtimeResult.Data is not null)
                {
                    guidance.Add(runtimeResult.Data.Guidance);
                    continue;
                }
            }

            using var manifest = JsonDocument.Parse(pack.Json);
            var root = manifest.RootElement;
            var path = PackContentPathResolver.ResolveVersionedRelativePath(pack.Name, pack.Version, root);
            var url = $"{_options.PacksBaseUrl.TrimEnd('/')}/packs/{Uri.EscapeDataString(pack.Name)}/{path}";
            using var request = BuildRequest(url, pack.Name);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Baseline pack content fetch failed for {PackName}. Url {Url}. Status {StatusCode}.",
                    pack.Name,
                    url,
                    response.StatusCode);
                return ServiceResponse<IReadOnlyList<BaselinePackGuidance>>.Fail(
                    new DomainError(ErrorCodes.DependencyUnavailable, "Baseline pack content unavailable."),
                    statusCode: (int)response.StatusCode,
                    message: "Baseline pack content unavailable");
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            content = NormalizeContent(content, _options.BaselinePackContentMaxChars);
            if (!string.IsNullOrWhiteSpace(content))
            {
                var metadata = PackManifestReader.Read(root);
                guidance.Add(new BaselinePackGuidance(
                    pack.Name,
                    pack.Version,
                    pack.Hash,
                    content,
                    packFamily: metadata.PackFamily,
                    moduleRole: metadata.ModuleRoles.FirstOrDefault(),
                    applyStage: metadata.ApplyStage,
                    hookName: metadata.HookName,
                    alwaysLoad: metadata.AlwaysLoad,
                    runtimeRequired: metadata.RuntimeRequired,
                    forceMatchQueries: metadata.ForceMatchQueries,
                    confidenceFloor: metadata.ConfidenceFloor));
            }
        }

        return ServiceResponse<IReadOnlyList<BaselinePackGuidance>>.Success(guidance);
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

    private static string NormalizeContent(string content, int maxChars)
    {
        var trimmed = content.Trim();
        if (trimmed.Length <= maxChars)
        {
            return trimmed;
        }

        return trimmed[..maxChars].Trim();
    }
}
