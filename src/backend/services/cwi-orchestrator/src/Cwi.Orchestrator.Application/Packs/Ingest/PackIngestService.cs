#nullable enable
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Cwi.Common;
using Cwi.Common.Errors;

namespace Cwi.Orchestrator.Application.Packs.Ingest;

public sealed class PackIngestService : DomainServiceBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly ApplicationOptions _options;
    private readonly PackSchemaValidator _schemaValidator;

    public PackIngestService(ApplicationOptions options, PackSchemaValidator schemaValidator)
    {
        _options = options;
        _schemaValidator = schemaValidator;
    }

    public Task<ServiceResponse<PackIngestResponseDto>> HandleAsync(
        PackIngestRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleInternalAsync(request, cancellationToken));
    }

    private async Task<ServiceResponse<PackIngestResponseDto>> HandleInternalAsync(
        PackIngestRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.BundlePath))
        {
            return ServiceResponse<PackIngestResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Bundle path is required."),
                statusCode: 400,
                message: "Bundle path required");
        }

        if (!File.Exists(request.BundlePath))
        {
            return ServiceResponse<PackIngestResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Bundle file not found."),
                statusCode: 400,
                message: "Bundle file not found");
        }

        var isZip = await IsZipBundleAsync(request.BundlePath, cancellationToken).ConfigureAwait(false);
        var packJson = isZip
            ? await ReadPackJsonFromZipAsync(request.BundlePath, cancellationToken).ConfigureAwait(false)
            : await File.ReadAllTextAsync(request.BundlePath, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(packJson))
        {
            return ServiceResponse<PackIngestResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "pack.json missing from bundle."),
                statusCode: 400,
                message: "pack.json missing");
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(packJson);
        }
        catch (JsonException)
        {
            return ServiceResponse<PackIngestResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "pack.json is invalid JSON."),
                statusCode: 400,
                message: "pack.json invalid");
        }

        var schemaResult = _schemaValidator.Validate(node);
        if (!schemaResult.IsSuccess)
        {
            return ServiceResponse<PackIngestResponseDto>.Fail(
            schemaResult.Error ?? new DomainError(ErrorCodes.InvalidRequest, "Pack schema validation failed."),
            schemaResult.StatusCode,
            schemaResult.Message ?? "Pack schema validation failed");
        }

        using var document = JsonDocument.Parse(packJson);
        var root = document.RootElement;
        var packId = TryGetString(root, "pack_id") ?? string.Empty;
        var packVersion = TryGetString(root, "pack_version") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(packId) || string.IsNullOrWhiteSpace(packVersion))
        {
            return ServiceResponse<PackIngestResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "pack_id and pack_version are required."),
                statusCode: 400,
                message: "pack_id and pack_version required");
        }

        if (!string.IsNullOrWhiteSpace(request.PackIdOverride) &&
            !string.Equals(request.PackIdOverride.Trim(), packId, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResponse<PackIngestResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "pack_id does not match bundle."),
                statusCode: 400,
                message: "pack_id mismatch");
        }

        if (!string.IsNullOrWhiteSpace(request.PackVersionOverride) &&
            !string.Equals(request.PackVersionOverride.Trim(), packVersion, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResponse<PackIngestResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "pack_version does not match bundle."),
                statusCode: 400,
                message: "pack_version mismatch");
        }

        if (!IsSafeSegment(packId) || !IsSafeSegment(packVersion))
        {
            return ServiceResponse<PackIngestResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "pack_id and pack_version must be URL-safe."),
                statusCode: 400,
                message: "pack_id or pack_version invalid");
        }

        var anchor = ComputeAnchor(root);

        var response = new PackIngestResponseDto
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
            return ServiceResponse<PackIngestResponseDto>.Success(response);
        }

        if (string.IsNullOrWhiteSpace(_options.PacksDirectory))
        {
            return ServiceResponse<PackIngestResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "PACKS_DIR is not configured."),
                statusCode: 500,
                message: "Packs directory missing");
        }

        var packsRoot = _options.PacksDirectory;
        Directory.CreateDirectory(packsRoot);

        var packRoot = Path.Combine(packsRoot, packId);
        var versionRoot = Path.Combine(packRoot, packVersion);
        var latestRoot = Path.Combine(packRoot, "latest");
        Directory.CreateDirectory(packRoot);
        Directory.CreateDirectory(versionRoot);
        Directory.CreateDirectory(latestRoot);

        if (isZip)
        {
            await ExtractZipAsync(request.BundlePath, versionRoot, cancellationToken).ConfigureAwait(false);
        }

        var versionPackPath = Path.Combine(versionRoot, "pack.json");
        await File.WriteAllTextAsync(versionPackPath, packJson, cancellationToken).ConfigureAwait(false);
        var versionAnchorPath = Path.Combine(versionRoot, "anchor.txt");
        await File.WriteAllTextAsync(versionAnchorPath, anchor, cancellationToken).ConfigureAwait(false);

        var rootPackPath = Path.Combine(packRoot, "pack.json");
        await File.WriteAllTextAsync(rootPackPath, packJson, cancellationToken).ConfigureAwait(false);
        var rootAnchorPath = Path.Combine(packRoot, "anchor.txt");
        await File.WriteAllTextAsync(rootAnchorPath, anchor, cancellationToken).ConfigureAwait(false);

        var latestPackPath = Path.Combine(latestRoot, "pack.json");
        await File.WriteAllTextAsync(latestPackPath, packJson, cancellationToken).ConfigureAwait(false);
        var latestAnchorPath = Path.Combine(latestRoot, "anchor.txt");
        await File.WriteAllTextAsync(latestAnchorPath, anchor, cancellationToken).ConfigureAwait(false);

        await UpdateIndexAsync(packsRoot, root, packId, packVersion, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<PackIngestResponseDto>.Success(response);
    }

    private static async Task<bool> IsZipBundleAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        var header = new byte[4];
        var read = await stream.ReadAsync(header, cancellationToken).ConfigureAwait(false);
        return read == 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04;
    }

    private static async Task<string?> ReadPackJsonFromZipAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.Entries.FirstOrDefault(e =>
            !string.IsNullOrWhiteSpace(e.FullName) &&
            !e.FullName.EndsWith("/", StringComparison.Ordinal) &&
            string.Equals(Path.GetFileName(e.FullName), "pack.json", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        await using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ExtractZipAsync(string path, string destinationRoot, CancellationToken cancellationToken)
    {
        var rootFullPath = Path.GetFullPath(destinationRoot);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName))
            {
                continue;
            }

            if (entry.FullName.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!destinationPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var entryStream = entry.Open();
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
            await entryStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task UpdateIndexAsync(
        string packsRoot,
        JsonElement packRoot,
        string packId,
        string packVersion,
        CancellationToken cancellationToken)
    {
        var indexPath = Path.Combine(packsRoot, "index.json");
        PackIndex index;
        if (File.Exists(indexPath))
        {
            try
            {
                var raw = await File.ReadAllTextAsync(indexPath, cancellationToken).ConfigureAwait(false);
                index = JsonSerializer.Deserialize<PackIndex>(raw, JsonOptions) ?? new PackIndex();
            }
            catch (JsonException)
            {
                index = new PackIndex();
            }
        }
        else
        {
            index = new PackIndex();
        }

        var entry = index.Packs.FirstOrDefault(p => string.Equals(p.PackId, packId, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            entry = new PackIndexEntry
            {
                PackId = packId,
                Name = TryGetString(packRoot, "name") ?? packId,
                Visibility = TryGetString(packRoot, "visibility") ?? "public",
                Latest = packVersion,
                Versions = new List<string> { packVersion }
            };
            index.Packs.Add(entry);
        }
        else
        {
            entry.Name = TryGetString(packRoot, "name") ?? entry.Name;
            entry.Visibility = TryGetString(packRoot, "visibility") ?? entry.Visibility;
            entry.Latest = packVersion;
            entry.Versions ??= new List<string>();
            if (!entry.Versions.Contains(packVersion, StringComparer.OrdinalIgnoreCase))
            {
                entry.Versions.Add(packVersion);
            }
        }

        var updated = JsonSerializer.Serialize(index, JsonOptions);
        await File.WriteAllTextAsync(indexPath, updated, cancellationToken).ConfigureAwait(false);
    }

    private static string ComputeAnchor(JsonElement element)
    {
        return PackAnchor.ComputeHash(element);
    }

    private static string? TryGetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static bool IsSafeSegment(string value)
    {
        return value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');
    }

    private sealed class PackIndex
    {
        [JsonPropertyName("packs")]
        public List<PackIndexEntry> Packs { get; set; } = new();
    }

    private sealed class PackIndexEntry
    {
        [JsonPropertyName("pack_id")]
        public string PackId { get; set; } = string.Empty;
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("latest")]
        public string? Latest { get; set; }
        [JsonPropertyName("versions")]
        public List<string>? Versions { get; set; }
        [JsonPropertyName("visibility")]
        public string? Visibility { get; set; }
        [JsonPropertyName("retired")]
        public bool? Retired { get; set; }
        [JsonPropertyName("retired_utc")]
        public DateTimeOffset? RetiredUtc { get; set; }
    }
}
#nullable disable
