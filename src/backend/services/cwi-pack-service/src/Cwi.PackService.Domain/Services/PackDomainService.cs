using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.PackService.Domain.Models;
using Cwi.PackService.Domain.Storage;
using Cwi.PackService.Domain.Validation;

namespace Cwi.PackService.Domain.Services;

public sealed class PackDomainService
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IPackRepository _repository;
    private readonly UploadConfig _uploadConfig;

    public PackDomainService(IPackRepository repository, UploadConfig uploadConfig)
    {
        _repository = repository;
        _uploadConfig = uploadConfig;
    }

    public ServiceResponse<PackFileDescriptor> GetIndex()
    {
        var path = _repository.ResolvePath("index.json");
        if (path is null)
        {
            return Fail<PackFileDescriptor>("invalid_path", "Invalid pack path.", 400);
        }

        if (!_repository.FileExists(path))
        {
            return Fail<PackFileDescriptor>("not_found", "Resource not found.", 404);
        }

        var etag = _repository.ComputeEtag(null, path);
        return ServiceResponse<PackFileDescriptor>.Success(new PackFileDescriptor(path, "application/json", null, etag));
    }

    public async Task<ServiceResponse<PackIndex>> GetValidatedIndexAsync(CancellationToken cancellationToken)
    {
        var path = _repository.ResolvePath("index.json");
        if (path is null)
        {
            return Fail<PackIndex>("invalid_path", "Invalid pack path.", 400);
        }

        if (!_repository.FileExists(path))
        {
            return Fail<PackIndex>("not_found", "Resource not found.", 404);
        }

        var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
        var filtered = new PackIndex();
        var availabilityCache = new Dictionary<string, Task<bool>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in index.Packs)
        {
            if (string.IsNullOrWhiteSpace(entry.PackId))
            {
                continue;
            }

            if (await IsPackDependencyGraphAvailableAsync(
                    entry.PackId,
                    availabilityCache,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    cancellationToken).ConfigureAwait(false))
            {
                filtered.Packs.Add(entry);
            }
        }

        return ServiceResponse<PackIndex>.Success(filtered, 200, "OK");
    }

    public Task<ServiceResponse<PackFileDescriptor>> GetPackJsonAsync(string pack, CancellationToken cancellationToken)
        => GetValidatedPackDescriptorAsync(pack, version: null, cancellationToken);

    public Task<ServiceResponse<PackFileDescriptor>> GetVersionedPackAsync(string pack, string version, CancellationToken cancellationToken)
        => GetValidatedPackDescriptorAsync(pack, version, cancellationToken);

    private async Task<ServiceResponse<PackFileDescriptor>> GetValidatedPackDescriptorAsync(
        string pack,
        string? version,
        CancellationToken cancellationToken)
    {
        if (!IsSafeSegment(pack) || (version is not null && !IsSafeSegment(version)))
        {
            return Fail<PackFileDescriptor>("invalid_pack", "Invalid pack path.", 400);
        }

        var packPath = version is null
            ? _repository.ResolvePath(pack, "pack.json")
            : _repository.ResolvePath(pack, version, "pack.json");
        if (packPath is null)
        {
            return Fail<PackFileDescriptor>("invalid_path", "Invalid pack path.", 400);
        }

        if (!_repository.FileExists(packPath))
        {
            return Fail<PackFileDescriptor>("not_found", "Resource not found.", 404);
        }

        var availabilityCache = new Dictionary<string, Task<bool>>(StringComparer.OrdinalIgnoreCase);
        if (!await IsPackDependencyGraphAvailableAsync(
                pack,
                version,
                availabilityCache,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                cancellationToken).ConfigureAwait(false))
        {
            return Fail<PackFileDescriptor>("not_found", "Resource not found.", 404);
        }

        var anchorPath = version is null
            ? _repository.ResolvePath(pack, "anchor.txt")
            : _repository.ResolvePath(pack, version, "anchor.txt");
        var etag = _repository.ComputeEtag(anchorPath, packPath);
        return ServiceResponse<PackFileDescriptor>.Success(new PackFileDescriptor(packPath, "application/json", anchorPath, etag));
    }

    public ServiceResponse<PackFileDescriptor> GetAnchor(string pack, string anchorRelativePath)
    {
        if (!IsSafeSegment(pack))
        {
            return Fail<PackFileDescriptor>("invalid_pack", "Invalid pack name.", 400);
        }

        var segments = anchorRelativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var path = _repository.ResolvePath(new[] { pack }.Concat(segments).ToArray());
        if (path is null)
        {
            return Fail<PackFileDescriptor>("invalid_path", "Invalid pack path.", 400);
        }

        if (!_repository.FileExists(path))
        {
            return Fail<PackFileDescriptor>("not_found", "Resource not found.", 404);
        }

        var etag = _repository.ComputeEtag(path, path);
        return ServiceResponse<PackFileDescriptor>.Success(new PackFileDescriptor(path, "text/plain", path, etag));
    }

    public ServiceResponse<PackFileDescriptor> GetContentFile(string pack, string version, string relativePath)
    {
        if (!IsSafeSegment(pack) || !IsSafeSegment(version) || !IsSafeRelativePath(relativePath))
        {
            return Fail<PackFileDescriptor>("invalid_pack", "Invalid pack content path.", 400);
        }

        var relativeSegments = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var path = _repository.ResolvePath(new[] { pack, version, "content" }.Concat(relativeSegments).ToArray());
        if (path is null)
        {
            return Fail<PackFileDescriptor>("invalid_path", "Invalid pack content path.", 400);
        }

        if (!_repository.FileExists(path))
        {
            return Fail<PackFileDescriptor>("not_found", "Resource not found.", 404);
        }

        var anchorPath = _repository.ResolvePath(pack, version, "anchor.txt");
        var contentType = ResolveContentType(path);
        var etag = _repository.ComputeEtag(anchorPath, path);
        return ServiceResponse<PackFileDescriptor>.Success(new PackFileDescriptor(path, contentType, anchorPath, etag));
    }

    public async Task<ServiceResponse<PackIngestResponse>> IngestPackAsync(PackUploadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.PackJson))
            {
                return Fail<PackIngestResponse>("invalid_pack", "pack.json is required.", 400);
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(request.PackJson);
            }
            catch (JsonException)
            {
                return Fail<PackIngestResponse>("invalid_pack", "pack.json is invalid JSON.", 400);
            }

            var schemaResult = PackServiceState.SchemaValidator.Validate(node);
            if (!schemaResult.IsSuccess)
            {
                return ServiceResponse<PackIngestResponse>.Fail(
                    schemaResult.Error ?? new DomainError("schema_invalid", "Pack schema validation failed."),
                    schemaResult.StatusCode,
                    schemaResult.Message);
            }

            using var document = JsonDocument.Parse(request.PackJson);
            var root = document.RootElement;
            var packId = TryGetString(root, "pack_id") ?? string.Empty;
            var packVersion = TryGetString(root, "pack_version") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(packId) || string.IsNullOrWhiteSpace(packVersion))
            {
                return Fail<PackIngestResponse>("invalid_pack", "pack_id and pack_version are required.", 400);
            }

            if (!string.IsNullOrWhiteSpace(request.PackIdOverride) &&
                !string.Equals(request.PackIdOverride.Trim(), packId, StringComparison.OrdinalIgnoreCase))
            {
                return Fail<PackIngestResponse>("invalid_pack", "pack_id does not match payload.", 400);
            }

            if (!string.IsNullOrWhiteSpace(request.PackVersionOverride) &&
                !string.Equals(request.PackVersionOverride.Trim(), packVersion, StringComparison.OrdinalIgnoreCase))
            {
                return Fail<PackIngestResponse>("invalid_pack", "pack_version does not match payload.", 400);
            }

            if (!IsSafeSegment(packId) || !IsSafeSegment(packVersion))
            {
                return Fail<PackIngestResponse>("invalid_pack", "pack_id and pack_version must be URL-safe.", 400);
            }

            var canonical = Canonicalize(root);
            var anchor = ComputeAnchor(canonical);

            var response = new PackIngestResponse
            {
                PackId = packId,
                PackVersion = packVersion,
                Anchor = anchor,
                DryRun = request.DryRun,
                Notes = request.Notes,
                UploadedUtc = DateTimeOffset.UtcNow,
                Message = request.DryRun ? "Dry run completed." : "Pack ingested."
            };

            if (request.DryRun)
            {
                return ServiceResponse<PackIngestResponse>.Success(response, 200, "OK");
            }

            var packRoot = _repository.ResolvePath(packId);
            var versionRoot = _repository.ResolvePath(packId, packVersion);
            var latestRoot = _repository.ResolvePath(packId, "latest");

            if (packRoot is null || versionRoot is null || latestRoot is null)
            {
                return Fail<PackIngestResponse>("invalid_path", "Invalid pack path.", 400);
            }

            var versionPackPath = _repository.ResolvePath(packId, packVersion, "pack.json");
            if (versionPackPath is null)
            {
                return Fail<PackIngestResponse>("invalid_path", "Invalid pack path.", 400);
            }

            if (_repository.FileExists(versionPackPath))
            {
                return Fail<PackIngestResponse>("conflict", "Pack version already exists.", 409);
            }

            await PackServiceState.IndexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _repository.EnsureDirectory(packRoot);
                _repository.EnsureDirectory(versionRoot);
                _repository.EnsureDirectory(latestRoot);

                if (!string.IsNullOrWhiteSpace(request.BundlePath) && request.IsZip)
                {
                    await _repository.ExtractZipAsync(request.BundlePath, versionRoot, _uploadConfig, cancellationToken)
                        .ConfigureAwait(false);
                }

                var versionAnchorPath = _repository.ResolvePath(packId, packVersion, "anchor.txt");
                var rootPackPath = _repository.ResolvePath(packId, "pack.json");
                var rootAnchorPath = _repository.ResolvePath(packId, "anchor.txt");
                var latestPackPath = _repository.ResolvePath(packId, "latest", "pack.json");
                var latestAnchorPath = _repository.ResolvePath(packId, "latest", "anchor.txt");

                if (versionAnchorPath is null || rootPackPath is null || rootAnchorPath is null ||
                    latestPackPath is null || latestAnchorPath is null)
                {
                    return Fail<PackIngestResponse>("invalid_path", "Invalid pack path.", 400);
                }

                await _repository.WriteAllTextAtomicAsync(versionPackPath, canonical, cancellationToken).ConfigureAwait(false);
                await _repository.WriteAllTextAtomicAsync(versionAnchorPath, anchor, cancellationToken).ConfigureAwait(false);
                await _repository.WriteAllTextAtomicAsync(rootPackPath, canonical, cancellationToken).ConfigureAwait(false);
                await _repository.WriteAllTextAtomicAsync(rootAnchorPath, anchor, cancellationToken).ConfigureAwait(false);
                await _repository.WriteAllTextAtomicAsync(latestPackPath, canonical, cancellationToken).ConfigureAwait(false);
                await _repository.WriteAllTextAtomicAsync(latestAnchorPath, anchor, cancellationToken).ConfigureAwait(false);

                var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
                var entry = index.Packs.FirstOrDefault(p => string.Equals(p.PackId, packId, StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    entry = new PackIndexEntry
                    {
                        PackId = packId,
                        Name = TryGetString(root, "name") ?? packId,
                        Visibility = TryGetString(root, "visibility") ?? "public",
                        Latest = packVersion,
                        Versions = new List<string> { packVersion },
                        Retired = false,
                        RetiredUtc = null
                    };
                    index.Packs.Add(entry);
                }
                else
                {
                    entry.Name = TryGetString(root, "name") ?? entry.Name;
                    entry.Visibility = TryGetString(root, "visibility") ?? entry.Visibility;
                    entry.Latest = packVersion;
                    entry.Versions ??= new List<string>();
                    if (!entry.Versions.Contains(packVersion, StringComparer.OrdinalIgnoreCase))
                    {
                        entry.Versions.Add(packVersion);
                    }
                    entry.Retired = false;
                    entry.RetiredUtc = null;
                }

                await SaveIndexAsync(index, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                PackServiceState.IndexLock.Release();
            }

            return ServiceResponse<PackIngestResponse>.Success(response, 200, "OK");
        }
        catch (PayloadTooLargeException)
        {
            return Fail<PackIngestResponse>("payload_too_large", "Upload exceeds allowed size.", 413);
        }
        catch (InvalidDataException)
        {
            return Fail<PackIngestResponse>("invalid_pack", "Pack bundle is invalid.", 400);
        }
        catch (Exception)
        {
            return Fail<PackIngestResponse>("ingest_failed", "Pack ingest failed.", 500);
        }
    }

    public async Task<ServiceResponse<PackIndexEntry>> UpdatePackMetadataAsync(
        string pack,
        string? name,
        string? visibility,
        bool? retired,
        bool requireBreakGlass,
        CancellationToken cancellationToken)
    {
        if (!IsSafeSegment(pack))
        {
            return Fail<PackIndexEntry>("invalid_pack", "Invalid pack name.", 400);
        }

        if (name is null && visibility is null && retired is null)
        {
            return Fail<PackIndexEntry>("invalid_request", "No updates provided.", 400);
        }

        if (retired is not null && requireBreakGlass)
        {
            return Fail<PackIndexEntry>("break_glass_required", "Break-glass token required.", 403);
        }

        await PackServiceState.IndexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
            var entry = index.Packs.FirstOrDefault(p => string.Equals(p.PackId, pack, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return Fail<PackIndexEntry>("not_found", "Pack not found.", 404);
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                entry.Name = name;
            }

            if (!string.IsNullOrWhiteSpace(visibility))
            {
                entry.Visibility = visibility;
            }

            if (retired is not null)
            {
                entry.Retired = retired.Value;
                entry.RetiredUtc = retired.Value ? DateTimeOffset.UtcNow : null;
            }

            await SaveIndexAsync(index, cancellationToken).ConfigureAwait(false);

            return ServiceResponse<PackIndexEntry>.Success(entry, 200, "OK");
        }
        catch (Exception)
        {
            return Fail<PackIndexEntry>("update_failed", "Pack update failed.", 500);
        }
        finally
        {
            PackServiceState.IndexLock.Release();
        }
    }

    public async Task<ServiceResponse<PackIndexEntry>> PromotePackVersionAsync(
        string pack,
        string version,
        bool setLatest,
        CancellationToken cancellationToken)
    {
        if (!IsSafeSegment(pack) || !IsSafeSegment(version))
        {
            return Fail<PackIndexEntry>("invalid_pack", "Invalid pack path.", 400);
        }

        if (!setLatest)
        {
            return Fail<PackIndexEntry>("invalid_request", "set_latest=true is required.", 400);
        }

        await PackServiceState.IndexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
            var entry = index.Packs.FirstOrDefault(p => string.Equals(p.PackId, pack, StringComparison.OrdinalIgnoreCase));
            if (entry is null || entry.Versions is null || !entry.Versions.Contains(version, StringComparer.OrdinalIgnoreCase))
            {
                return Fail<PackIndexEntry>("not_found", "Pack version not found.", 404);
            }

            if (!await PromoteLatestAsync(pack, version, cancellationToken).ConfigureAwait(false))
            {
                return Fail<PackIndexEntry>("not_found", "Pack version assets missing.", 404);
            }

            entry.Latest = version;
            await SaveIndexAsync(index, cancellationToken).ConfigureAwait(false);

            return ServiceResponse<PackIndexEntry>.Success(entry, 200, "OK");
        }
        catch (Exception)
        {
            return Fail<PackIndexEntry>("update_failed", "Pack update failed.", 500);
        }
        finally
        {
            PackServiceState.IndexLock.Release();
        }
    }

    public async Task<ServiceResponse<PackDeleteResult>> DeletePackAsync(string pack, CancellationToken cancellationToken)
    {
        if (!IsSafeSegment(pack))
        {
            return Fail<PackDeleteResult>("invalid_pack", "Invalid pack name.", 400);
        }

        var packRoot = _repository.ResolvePath(pack);
        if (packRoot is null)
        {
            return Fail<PackDeleteResult>("invalid_path", "Invalid pack path.", 400);
        }

        await PackServiceState.IndexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
            var entry = index.Packs.FirstOrDefault(p => string.Equals(p.PackId, pack, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return Fail<PackDeleteResult>("not_found", "Pack not found.", 404);
            }

            if (_repository.DirectoryExists(packRoot))
            {
                _repository.DeleteDirectory(packRoot);
            }

            index.Packs.Remove(entry);
            await SaveIndexAsync(index, cancellationToken).ConfigureAwait(false);

            return ServiceResponse<PackDeleteResult>.Success(new PackDeleteResult
            {
                Deleted = true,
                PackId = pack
            });
        }
        catch (Exception)
        {
            return Fail<PackDeleteResult>("delete_failed", "Pack delete failed.", 500);
        }
        finally
        {
            PackServiceState.IndexLock.Release();
        }
    }

    public async Task<ServiceResponse<PackDeleteResult>> DeletePackVersionAsync(string pack, string version, CancellationToken cancellationToken)
    {
        if (!IsSafeSegment(pack) || !IsSafeSegment(version))
        {
            return Fail<PackDeleteResult>("invalid_pack", "Invalid pack path.", 400);
        }

        var versionRoot = _repository.ResolvePath(pack, version);
        if (versionRoot is null)
        {
            return Fail<PackDeleteResult>("invalid_path", "Invalid pack path.", 400);
        }

        await PackServiceState.IndexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var index = await LoadIndexAsync(cancellationToken).ConfigureAwait(false);
            var entry = index.Packs.FirstOrDefault(p => string.Equals(p.PackId, pack, StringComparison.OrdinalIgnoreCase));
            if (entry is null || entry.Versions is null || !entry.Versions.Contains(version, StringComparer.OrdinalIgnoreCase))
            {
                return Fail<PackDeleteResult>("not_found", "Pack version not found.", 404);
            }

            if (_repository.DirectoryExists(versionRoot))
            {
                _repository.DeleteDirectory(versionRoot);
            }

            entry.Versions.RemoveAll(v => string.Equals(v, version, StringComparison.OrdinalIgnoreCase));

            if (entry.Versions.Count == 0)
            {
                index.Packs.Remove(entry);
            }
            else if (string.Equals(entry.Latest, version, StringComparison.OrdinalIgnoreCase))
            {
                var newLatest = entry.Versions.Last();
                entry.Latest = newLatest;
                await PromoteLatestAsync(pack, newLatest, cancellationToken).ConfigureAwait(false);
            }

            await SaveIndexAsync(index, cancellationToken).ConfigureAwait(false);

            return ServiceResponse<PackDeleteResult>.Success(new PackDeleteResult
            {
                Deleted = true,
                PackId = pack,
                Version = version
            });
        }
        catch (Exception)
        {
            return Fail<PackDeleteResult>("delete_failed", "Pack delete failed.", 500);
        }
        finally
        {
            PackServiceState.IndexLock.Release();
        }
    }

    private async Task<PackIndex> LoadIndexAsync(CancellationToken cancellationToken)
    {
        var indexPath = _repository.ResolvePath("index.json");
        if (indexPath is null || !_repository.FileExists(indexPath))
        {
            return new PackIndex();
        }

        try
        {
            var raw = await _repository.ReadAllTextAsync(indexPath, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<PackIndex>(raw, PackServiceState.IndexJsonOptions) ?? new PackIndex();
        }
        catch (JsonException)
        {
            return new PackIndex();
        }
    }

    private Task<bool> IsPackDependencyGraphAvailableAsync(
        string packId,
        IDictionary<string, Task<bool>> availabilityCache,
        HashSet<string> chain,
        CancellationToken cancellationToken)
    {
        return IsPackDependencyGraphAvailableAsync(packId, version: null, availabilityCache, chain, cancellationToken);
    }

    private Task<bool> IsPackDependencyGraphAvailableAsync(
        string packId,
        string? version,
        IDictionary<string, Task<bool>> availabilityCache,
        HashSet<string> chain,
        CancellationToken cancellationToken)
    {
        if (!IsSafeSegment(packId) || (version is not null && !IsSafeSegment(version)))
        {
            return Task.FromResult(false);
        }

        var normalizedPackId = packId.Trim();
        var normalizedVersion = version?.Trim();
        var cacheKey = normalizedVersion is null ? normalizedPackId : $"{normalizedPackId}@{normalizedVersion}";
        if (availabilityCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var task = IsPackDependencyGraphAvailableCoreAsync(
            normalizedPackId,
            normalizedVersion,
            availabilityCache,
            chain,
            cancellationToken);
        availabilityCache[cacheKey] = task;
        return task;
    }

    private async Task<bool> IsPackDependencyGraphAvailableCoreAsync(
        string packId,
        string? version,
        IDictionary<string, Task<bool>> availabilityCache,
        HashSet<string> chain,
        CancellationToken cancellationToken)
    {
        var chainKey = version is null ? packId : $"{packId}@{version}";
        if (!chain.Add(chainKey))
        {
            return true;
        }

        try
        {
            var dependencies = await LoadPackDependenciesAsync(packId, version, cancellationToken).ConfigureAwait(false);
            if (dependencies is null)
            {
                return false;
            }

            foreach (var dependency in dependencies)
            {
                if (!await IsPackDependencyGraphAvailableAsync(
                        dependency,
                        version: null,
                        availabilityCache,
                        chain,
                        cancellationToken)
                        .ConfigureAwait(false))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            chain.Remove(chainKey);
        }
    }

    private async Task<IReadOnlyList<string>?> LoadPackDependenciesAsync(
        string packId,
        string? version,
        CancellationToken cancellationToken)
    {
        var packPath = version is null
            ? _repository.ResolvePath(packId, "pack.json")
            : _repository.ResolvePath(packId, version, "pack.json");
        if (packPath is null || !_repository.FileExists(packPath))
        {
            return null;
        }

        return await LoadPackDependenciesFromPathAsync(packPath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>?> LoadPackDependenciesFromPathAsync(string packPath, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await _repository.ReadAllTextAsync(packPath, cancellationToken).ConfigureAwait(false);
            var node = JsonNode.Parse(raw);
            var schemaResult = PackServiceState.SchemaValidator.Validate(node);
            if (!schemaResult.IsSuccess)
            {
                return null;
            }

            using var document = JsonDocument.Parse(raw);
            return ReadDependencies(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadDependencies(JsonElement root)
    {
        if (!root.TryGetProperty("dependencies", out var dependencies))
        {
            return Array.Empty<string>();
        }

        if (dependencies.ValueKind == JsonValueKind.Array)
        {
            return ReadDependencyArray(dependencies);
        }

        if (dependencies.ValueKind == JsonValueKind.Object)
        {
            if (dependencies.TryGetProperty("packs", out var packs) && packs.ValueKind == JsonValueKind.Array)
            {
                return ReadDependencyArray(packs);
            }

            if (dependencies.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                return ReadDependencyArray(items);
            }
        }

        return Array.Empty<string>();
    }

    private static IReadOnlyList<string> ReadDependencyArray(JsonElement array)
    {
        var dependencies = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    dependencies.Add(value.Trim());
                }

                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = TryGetString(item, "pack_id") ?? TryGetString(item, "name") ?? TryGetString(item, "pack");
            if (!string.IsNullOrWhiteSpace(name))
            {
                dependencies.Add(name.Trim());
            }
        }

        return dependencies;
    }

    private async Task SaveIndexAsync(PackIndex index, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(index, PackServiceState.IndexJsonOptions);
        var indexPath = _repository.ResolvePath("index.json");
        if (indexPath is null)
        {
            return;
        }

        await _repository.WriteAllTextAtomicAsync(indexPath, json, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> PromoteLatestAsync(string packId, string version, CancellationToken cancellationToken)
    {
        var versionPackPath = _repository.ResolvePath(packId, version, "pack.json");
        var versionAnchorPath = _repository.ResolvePath(packId, version, "anchor.txt");
        var rootPackPath = _repository.ResolvePath(packId, "pack.json");
        var rootAnchorPath = _repository.ResolvePath(packId, "anchor.txt");
        var latestPackPath = _repository.ResolvePath(packId, "latest", "pack.json");
        var latestAnchorPath = _repository.ResolvePath(packId, "latest", "anchor.txt");

        if (versionPackPath is null || versionAnchorPath is null || rootPackPath is null || rootAnchorPath is null ||
            latestPackPath is null || latestAnchorPath is null)
        {
            return false;
        }

        if (!_repository.FileExists(versionPackPath) || !_repository.FileExists(versionAnchorPath))
        {
            return false;
        }

        var packJson = await _repository.ReadAllTextAsync(versionPackPath, cancellationToken).ConfigureAwait(false);
        var anchor = await _repository.ReadAllTextAsync(versionAnchorPath, cancellationToken).ConfigureAwait(false);

        await _repository.WriteAllTextAtomicAsync(rootPackPath, packJson, cancellationToken).ConfigureAwait(false);
        await _repository.WriteAllTextAtomicAsync(rootAnchorPath, anchor, cancellationToken).ConfigureAwait(false);
        await _repository.WriteAllTextAtomicAsync(latestPackPath, packJson, cancellationToken).ConfigureAwait(false);
        await _repository.WriteAllTextAtomicAsync(latestAnchorPath, anchor, cancellationToken).ConfigureAwait(false);

        return true;
    }

    private static string ComputeAnchor(string canonical)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Canonicalize(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => CanonicalizeObject(element),
            JsonValueKind.Array => CanonicalizeArray(element),
            JsonValueKind.String => JsonSerializer.Serialize(element.GetString(), CanonicalJsonOptions),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => element.GetRawText()
        };
    }

    private static string CanonicalizeObject(JsonElement element)
    {
        var props = element.EnumerateObject()
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        var builder = new StringBuilder();
        builder.Append('{');
        for (var i = 0; i < props.Count; i++)
        {
            var prop = props[i];
            builder.Append(JsonSerializer.Serialize(prop.Name, CanonicalJsonOptions));
            builder.Append(':');
            builder.Append(Canonicalize(prop.Value));
            if (i < props.Count - 1)
            {
                builder.Append(',');
            }
        }
        builder.Append('}');
        return builder.ToString();
    }

    private static string CanonicalizeArray(JsonElement element)
    {
        var items = element.EnumerateArray().ToList();
        var builder = new StringBuilder();
        builder.Append('[');
        for (var i = 0; i < items.Count; i++)
        {
            builder.Append(Canonicalize(items[i]));
            if (i < items.Count - 1)
            {
                builder.Append(',');
            }
        }
        builder.Append(']');
        return builder.ToString();
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static bool IsSafeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        if (segment is "." or "..")
        {
            return false;
        }

        return Regex.IsMatch(segment, "^[a-zA-Z0-9._-]+$");
    }

    private static bool IsSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var segments = relativePath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && segments.All(IsSafeSegment);
    }

    private static string ResolveContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".txt" or ".md" => "text/plain",
            ".json" => "application/json",
            _ => "application/octet-stream"
        };
    }

    private static ServiceResponse<T> Fail<T>(string code, string message, int statusCode)
    {
        return ServiceResponse<T>.Fail(new DomainError(code, message), statusCode, message);
    }
}
