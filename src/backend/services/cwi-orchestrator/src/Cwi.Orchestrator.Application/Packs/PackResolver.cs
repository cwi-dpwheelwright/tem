using Cwi.Common;
using Cwi.Common.Errors;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Cwi.Orchestrator.Application.Packs;

public interface IPackResolver
{
    Task<ServiceResponse<PackResolutionResult>> ResolveAsync(string packName, CancellationToken cancellationToken);
}

public sealed class PackResolver : IPackResolver
{
    private const int MaxDepth = 6;
    private readonly IReadOnlyDictionary<PackContractType, IPackContractResolver> _resolvers;
    private readonly ApplicationOptions _options;
    private readonly ICorePackRuntimeCatalog _corePackRuntimeCatalog;
    private readonly ILogger<PackResolver> _logger;

    public PackResolver(
        IEnumerable<IPackContractResolver> contractResolvers,
        ApplicationOptions options,
        ICorePackRuntimeCatalog corePackRuntimeCatalog,
        ILogger<PackResolver> logger)
    {
        _resolvers = contractResolvers.ToDictionary(resolver => resolver.ContractType, resolver => resolver);
        _options = options;
        _corePackRuntimeCatalog = corePackRuntimeCatalog;
        _logger = logger;
    }

    public async Task<ServiceResponse<PackResolutionResult>> ResolveAsync(string packName, CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dependencies = new List<PackDocument>();
        var baselinePacks = new List<PackDocument>();
        var capabilities = new List<PackCapability>();

        var chain = GetResolverChain();
        _logger.LogInformation(
            "Resolving pack {PackName} using resolver mode {Mode}. Chain: {Chain}.",
            packName,
            _options.PackResolverMode,
            FormatResolverChain(chain));
        var rootResult = await ResolvePackAsync(packName, cancellationToken).ConfigureAwait(false);
        if (!rootResult.IsSuccess || rootResult.Data is null)
        {
            if (CanUseSyntheticDefaultRoot(packName))
            {
                rootResult = ServiceResponse<PackDocument>.Success(BuildSyntheticDefaultRoot(packName));
                _logger.LogWarning(
                    "Default root pack {PackName} was unavailable. Using a synthetic runtime root so governed baseline packs can continue to load.",
                    packName);
            }
            else
            {
                return ServiceResponse<PackResolutionResult>.Fail(
                    rootResult.Error ?? new DomainError(ErrorCodes.DependencyUnavailable, "Pack fetch failed."),
                    statusCode: rootResult.StatusCode,
                    message: rootResult.Message);
            }
        }

        var root = rootResult.Data!;
        visited.Add(root.Name);

        var metadata = PackManifestReader.Read(root);
        if (!string.IsNullOrWhiteSpace(metadata.DeclaredName) &&
            !string.Equals(metadata.DeclaredName, packName, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Pack manifest name mismatch. Requested {Requested} vs declared {Declared}.",
                packName,
                metadata.DeclaredName);
            return ServiceResponse<PackResolutionResult>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Pack manifest name mismatch."),
                statusCode: 400,
                message: "Pack manifest name mismatch");
        }

        capabilities.AddRange(metadata.Capabilities);

        var dependencyResponse = await ResolveDependenciesAsync(
            metadata.Dependencies,
            visited,
            dependencies,
            capabilities,
            depth: 0,
            cancellationToken).ConfigureAwait(false);

        if (!dependencyResponse.IsSuccess)
        {
            return ServiceResponse<PackResolutionResult>.Fail(
                dependencyResponse.Error ?? new DomainError(ErrorCodes.DependencyUnavailable, "Pack dependency resolution failed."),
                statusCode: dependencyResponse.StatusCode,
                message: dependencyResponse.Message);
        }

        var baselineResponse = await ResolveBaselinePacksAsync(
            root,
            visited,
            dependencies,
            baselinePacks,
            capabilities,
            cancellationToken).ConfigureAwait(false);

        if (!baselineResponse.IsSuccess)
        {
            return ServiceResponse<PackResolutionResult>.Fail(
                baselineResponse.Error ?? new DomainError(ErrorCodes.DependencyUnavailable, "Baseline pack resolution failed."),
                statusCode: baselineResponse.StatusCode,
                message: baselineResponse.Message);
        }

        var capabilityMap = new PackCapabilityMap(capabilities);
        var orderedBaselinePacks = baselinePacks
            .Select((pack, index) => new
            {
                Pack = pack,
                Index = index,
                LoadOrder = ResolveBaselineLoadOrder(pack)
            })
            .OrderBy(item => item.LoadOrder)
            .ThenBy(item => item.Index)
            .Select(item => item.Pack)
            .ToList();

        return ServiceResponse<PackResolutionResult>.Success(
            new PackResolutionResult(root, dependencies, orderedBaselinePacks, capabilityMap));
    }

    private async Task<ServiceResponse<bool>> ResolveBaselinePacksAsync(
        PackDocument root,
        HashSet<string> visited,
        List<PackDocument> resolvedDependencies,
        List<PackDocument> baselinePacks,
        List<PackCapability> capabilities,
        CancellationToken cancellationToken)
    {
        if (_options.BaselinePackNames.Count == 0)
        {
            return ServiceResponse<bool>.Success(true);
        }

        foreach (var baselinePackName in _options.BaselinePackNames)
        {
            if (string.IsNullOrWhiteSpace(baselinePackName))
            {
                continue;
            }

            if (string.Equals(root.Name, baselinePackName, StringComparison.OrdinalIgnoreCase))
            {
                baselinePacks.Add(root);
                continue;
            }

            var alreadyResolved = resolvedDependencies.FirstOrDefault(pack =>
                string.Equals(pack.Name, baselinePackName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pack.Descriptor.RequestedName, baselinePackName, StringComparison.OrdinalIgnoreCase));
            if (alreadyResolved is not null)
            {
                baselinePacks.Add(alreadyResolved);
                continue;
            }

            if (!visited.Add(baselinePackName))
            {
                continue;
            }

            if (_corePackRuntimeCatalog.IsManaged(baselinePackName))
            {
                var runtimeResult = await _corePackRuntimeCatalog.GetAsync(baselinePackName, cancellationToken).ConfigureAwait(false);
                if (runtimeResult.IsSuccess && runtimeResult.Data is not null)
                {
                    baselinePacks.Add(runtimeResult.Data.Document);
                    resolvedDependencies.Add(runtimeResult.Data.Document);
                    var runtimeMetadata = PackManifestReader.Read(runtimeResult.Data.Document);
                    capabilities.AddRange(runtimeMetadata.Capabilities);
                    _logger.LogInformation(
                        "Resolved required baseline pack {PackName} via runtime catalog version {Version} hash {Hash}.",
                        runtimeResult.Data.Document.Name,
                        runtimeResult.Data.Document.Version,
                        runtimeResult.Data.Document.Hash);

                    var runtimeNested = await ResolveDependenciesAsync(
                        runtimeMetadata.Dependencies,
                        visited,
                        resolvedDependencies,
                        capabilities,
                        depth: 0,
                        cancellationToken).ConfigureAwait(false);

                    if (!runtimeNested.IsSuccess)
                    {
                        return runtimeNested;
                    }

                    continue;
                }

                _logger.LogWarning(
                    "Managed baseline pack {PackName} unavailable in runtime catalog. Falling back to resolver chain. Status {StatusCode}.",
                    baselinePackName,
                    runtimeResult.StatusCode);
            }

            var packResult = await ResolvePackAsync(baselinePackName, cancellationToken).ConfigureAwait(false);
            if (!packResult.IsSuccess || packResult.Data is null)
            {
                _logger.LogWarning(
                    "Required baseline pack {PackName} could not be resolved. Status {StatusCode}.",
                    baselinePackName,
                    packResult.StatusCode);
                return ServiceResponse<bool>.Fail(
                    packResult.Error ?? new DomainError(ErrorCodes.DependencyUnavailable, "Baseline pack fetch failed."),
                    statusCode: packResult.StatusCode,
                    message: "Baseline pack resolution failed");
            }

            baselinePacks.Add(packResult.Data);
            resolvedDependencies.Add(packResult.Data);
            var metadata = PackManifestReader.Read(packResult.Data);
            capabilities.AddRange(metadata.Capabilities);
            _logger.LogInformation(
                "Resolved required baseline pack {PackName} via {ContractType} version {Version} hash {Hash}.",
                packResult.Data.Name,
                packResult.Data.ContractType,
                packResult.Data.Version,
                packResult.Data.Hash);

            var nested = await ResolveDependenciesAsync(
                metadata.Dependencies,
                visited,
                resolvedDependencies,
                capabilities,
                depth: 0,
                cancellationToken).ConfigureAwait(false);

            if (!nested.IsSuccess)
            {
                return nested;
            }
        }

        return ServiceResponse<bool>.Success(true);
    }

    private static int ResolveBaselineLoadOrder(PackDocument pack)
    {
        var preferredOrder = CorePackOrdering.ResolveRequiredOrder(pack.Name);
        if (preferredOrder != int.MaxValue)
        {
            return preferredOrder;
        }

        return PackManifestReader.Read(pack).ActivationLoadOrder ?? int.MaxValue;
    }

    private static bool CanUseSyntheticDefaultRoot(string packName)
    {
        return string.Equals(packName, "general-pack", StringComparison.OrdinalIgnoreCase)
               || string.Equals(packName, "home-node-pack", StringComparison.OrdinalIgnoreCase);
    }

    private static PackDocument BuildSyntheticDefaultRoot(string packName)
    {
        var normalizedPackName = packName.Trim();
        const string version = "runtime-default";
        var manifest = new Dictionary<string, object?>
        {
            ["pack_id"] = normalizedPackName,
            ["pack_version"] = version,
            ["name"] = normalizedPackName,
            ["description"] = "Synthetic runtime root for generic governed CWI responses.",
            ["activation"] = new Dictionary<string, object?>
            {
                ["always_load"] = false,
                ["runtime_required"] = false
            },
            ["capabilities"] = Array.Empty<object>(),
            ["modules"] = Array.Empty<object>()
        };
        var json = JsonSerializer.Serialize(manifest);
        var hash = PackAnchor.ComputeHash(json);
        var descriptor = new PackDescriptor(
            normalizedPackName,
            normalizedPackName,
            version,
            hash,
            hash,
            PackContractType.Legacy,
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>());

        return new PackDocument(
            normalizedPackName,
            version,
            json,
            hash,
            hash,
            PackContractType.Legacy,
            descriptor);
    }

    private async Task<ServiceResponse<bool>> ResolveDependenciesAsync(
        IReadOnlyList<string> dependencies,
        HashSet<string> visited,
        List<PackDocument> resolvedDependencies,
        List<PackCapability> capabilities,
        int depth,
        CancellationToken cancellationToken)
    {
        if (dependencies.Count == 0)
        {
            return ServiceResponse<bool>.Success(true);
        }

        if (depth >= MaxDepth)
        {
            return ServiceResponse<bool>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Pack dependency depth exceeded."),
                statusCode: 400,
                message: "Pack dependency depth exceeded");
        }

        foreach (var dependency in dependencies)
        {
            if (string.IsNullOrWhiteSpace(dependency))
            {
                continue;
            }

            if (!visited.Add(dependency))
            {
                continue;
            }

            var packResult = await ResolvePackAsync(dependency, cancellationToken).ConfigureAwait(false);
            if (!packResult.IsSuccess || packResult.Data is null)
            {
                return ServiceResponse<bool>.Fail(
                    packResult.Error ?? new DomainError(ErrorCodes.DependencyUnavailable, "Pack dependency fetch failed."),
                    statusCode: packResult.StatusCode,
                    message: packResult.Message);
            }

            resolvedDependencies.Add(packResult.Data);
            var metadata = PackManifestReader.Read(packResult.Data);
            capabilities.AddRange(metadata.Capabilities);
            _logger.LogInformation(
                "Resolved dependency {Dependency} via {ContractType} version {Version} hash {Hash}.",
                packResult.Data.Name,
                packResult.Data.ContractType,
                packResult.Data.Version,
                packResult.Data.Hash);

            var nested = await ResolveDependenciesAsync(
                metadata.Dependencies,
                visited,
                resolvedDependencies,
                capabilities,
                depth + 1,
                cancellationToken).ConfigureAwait(false);

            if (!nested.IsSuccess)
            {
                return nested;
            }
        }

        return ServiceResponse<bool>.Success(true);
    }

    private async Task<ServiceResponse<PackDocument>> ResolvePackAsync(
        string packName,
        CancellationToken cancellationToken)
    {
        var chain = GetResolverChain();
        if (chain.Count == 0)
        {
            _logger.LogError("No pack resolvers configured. Pack {PackName} cannot be resolved.", packName);
            return ServiceResponse<PackDocument>.Fail(
                new DomainError(ErrorCodes.InternalError, "Pack resolvers unavailable."),
                statusCode: 500,
                message: "Pack resolvers unavailable");
        }
        ServiceResponse<PackDocument>? lastFailure = null;

        foreach (var resolver in chain)
        {
            var attempt = await resolver.ResolveAsync(packName, cancellationToken).ConfigureAwait(false);
            if (attempt.IsSuccess && attempt.Data is not null)
            {
                _logger.LogInformation(
                    "Resolved pack {PackName} via {ContractType} resolver.",
                    packName,
                    resolver.ContractType);
                return attempt;
            }

            lastFailure = attempt;
            if (!ShouldFallback(attempt))
            {
                _logger.LogWarning(
                    "Pack {PackName} resolution via {ContractType} failed without fallback. Status {StatusCode}. Error {ErrorCode}.",
                    packName,
                    resolver.ContractType,
                    attempt.StatusCode,
                    attempt.Error?.Code ?? "none");
                return attempt;
            }

            _logger.LogInformation(
                "Pack {PackName} not resolved via {ContractType} resolver. Status {StatusCode}. Attempting fallback.",
                packName,
                resolver.ContractType,
                attempt.StatusCode);
        }

        return lastFailure ?? ServiceResponse<PackDocument>.Fail(
            new DomainError(ErrorCodes.DependencyUnavailable, "Pack resolution failed."),
            statusCode: 503,
            message: "Pack resolution failed");
    }

    private IReadOnlyList<IPackContractResolver> GetResolverChain()
    {
        var legacy = GetResolver(PackContractType.Legacy);
        var adopted = GetResolver(PackContractType.Adopted);

        return _options.PackResolverMode switch
        {
            PackResolverMode.LegacyOnly => legacy is null ? Array.Empty<IPackContractResolver>() : new[] { legacy },
            PackResolverMode.AdoptedOnly => adopted is null ? Array.Empty<IPackContractResolver>() : new[] { adopted },
            PackResolverMode.LegacyFirst => BuildChain(legacy, adopted),
            PackResolverMode.AdoptedFirst => BuildChain(adopted, legacy),
            // "Auto" is governance-first, then falls back to legacy live-pack endpoints
            // when adopted metadata is temporarily unavailable.
            PackResolverMode.Auto => BuildChain(adopted, legacy),
            _ => adopted is null ? Array.Empty<IPackContractResolver>() : new[] { adopted }
        };
    }

    private IPackContractResolver? GetResolver(PackContractType type)
    {
        return _resolvers.TryGetValue(type, out var resolver) ? resolver : null;
    }

    private static IReadOnlyList<IPackContractResolver> BuildChain(IPackContractResolver? primary, IPackContractResolver? secondary)
    {
        if (primary is null && secondary is null)
        {
            return Array.Empty<IPackContractResolver>();
        }

        if (primary is null)
        {
            return new[] { secondary! };
        }

        if (secondary is null)
        {
            return new[] { primary };
        }

        return new[] { primary, secondary };
    }

    private static bool ShouldFallback(ServiceResponse<PackDocument> attempt)
    {
        if (attempt.IsSuccess)
        {
            return false;
        }

        if (attempt.StatusCode == 404 || attempt.StatusCode == 410)
        {
            return true;
        }

        if (attempt.Error?.Code == ErrorCodes.DependencyUnavailable)
        {
            return true;
        }

        return false;
    }

    private static string FormatResolverChain(IReadOnlyList<IPackContractResolver> chain)
    {
        if (chain.Count == 0)
        {
            return "none";
        }

        return string.Join(" -> ", chain.Select(resolver => resolver.ContractType.ToString()));
    }
}
