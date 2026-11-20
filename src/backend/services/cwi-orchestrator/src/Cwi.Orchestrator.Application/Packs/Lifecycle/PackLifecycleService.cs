#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using Cwi.Common;
using Cwi.Common.Errors;

namespace Cwi.Orchestrator.Application.Packs.Lifecycle;

public sealed class PackLifecycleService : DomainServiceBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ApplicationOptions _options;
    private readonly PackLifecycleAuditRecorder _auditRecorder;

    public PackLifecycleService(ApplicationOptions options, PackLifecycleAuditRecorder auditRecorder)
    {
        _options = options;
        _auditRecorder = auditRecorder;
    }

    public Task<ServiceResponse<PackRetireResponseDto>> RetireAsync(
        PackRetireRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleRetireAsync(request, cancellationToken));
    }

    public Task<ServiceResponse<PackDeleteResponseDto>> DeleteAsync(
        PackDeleteRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleDeleteAsync(request, cancellationToken));
    }

    private async Task<ServiceResponse<PackRetireResponseDto>> HandleRetireAsync(
        PackRetireRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PackId))
        {
            return Fail<PackRetireResponseDto>("pack_id required");
        }

        if (string.IsNullOrWhiteSpace(request.ActorUserId))
        {
            return Fail<PackRetireResponseDto>("actor_user_id required");
        }

        var packId = request.PackId.Trim();
        if (!IsSafeSegment(packId))
        {
            return Fail<PackRetireResponseDto>("pack_id invalid");
        }

        if (string.IsNullOrWhiteSpace(_options.PacksDirectory))
        {
            return Fail<PackRetireResponseDto>("PACKS_DIR is not configured.", statusCode: 500);
        }

        var packsRoot = _options.PacksDirectory;
        var indexResult = await LoadIndexAsync(packsRoot, cancellationToken).ConfigureAwait(false);
        if (!indexResult.IsSuccess || indexResult.Data is null)
        {
            return ServiceResponse<PackRetireResponseDto>.Fail(
                indexResult.Error ?? new DomainError(ErrorCodes.InvalidRequest, "pack index error"),
                indexResult.StatusCode,
                indexResult.Message ?? "pack index error");
        }

        var index = indexResult.Data;
        var entry = FindEntry(index, packId);
        if (entry is null)
        {
            return Fail<PackRetireResponseDto>("pack not found", statusCode: 404);
        }

        var now = DateTimeOffset.UtcNow;
        if (entry.Retired == true)
        {
            var retiredUtc = entry.RetiredUtc ?? now;
            return ServiceResponse<PackRetireResponseDto>.Success(new PackRetireResponseDto
            {
                PackId = entry.PackId,
                Status = "retired",
                Retired = true,
                RetiredUtc = retiredUtc
            });
        }

        entry.Retired = true;
        entry.RetiredUtc = now;
        await SaveIndexAsync(packsRoot, index, cancellationToken).ConfigureAwait(false);

        await _auditRecorder.RecordRetireAsync(new PackLifecycleAuditPayload
        {
            TenantId = request.TenantId,
            PackId = entry.PackId,
            ActorUserId = request.ActorUserId.Trim(),
            Reason = request.Reason,
            Action = "retire",
            TimestampUtc = now
        }, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<PackRetireResponseDto>.Success(new PackRetireResponseDto
        {
            PackId = entry.PackId,
            Status = "retired",
            Retired = true,
            RetiredUtc = now
        });
    }

    private async Task<ServiceResponse<PackDeleteResponseDto>> HandleDeleteAsync(
        PackDeleteRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PackId))
        {
            return Fail<PackDeleteResponseDto>("pack_id required");
        }

        if (string.IsNullOrWhiteSpace(request.ActorUserId))
        {
            return Fail<PackDeleteResponseDto>("actor_user_id required");
        }

        var packId = request.PackId.Trim();
        if (!IsSafeSegment(packId))
        {
            return Fail<PackDeleteResponseDto>("pack_id invalid");
        }

        var packVersion = string.IsNullOrWhiteSpace(request.PackVersion) ? null : request.PackVersion.Trim();
        if (!string.IsNullOrWhiteSpace(packVersion) && !IsSafeSegment(packVersion))
        {
            return Fail<PackDeleteResponseDto>("pack_version invalid");
        }

        if (string.IsNullOrWhiteSpace(_options.PacksDirectory))
        {
            return Fail<PackDeleteResponseDto>("PACKS_DIR is not configured.", statusCode: 500);
        }

        var packsRoot = _options.PacksDirectory;
        var indexResult = await LoadIndexAsync(packsRoot, cancellationToken).ConfigureAwait(false);
        if (!indexResult.IsSuccess || indexResult.Data is null)
        {
            return ServiceResponse<PackDeleteResponseDto>.Fail(
                indexResult.Error ?? new DomainError(ErrorCodes.InvalidRequest, "pack index error"),
                indexResult.StatusCode,
                indexResult.Message ?? "pack index error");
        }

        var index = indexResult.Data;
        var entry = FindEntry(index, packId);
        if (entry is null)
        {
            return Fail<PackDeleteResponseDto>("pack not found", statusCode: 404);
        }

        var packRoot = Path.Combine(packsRoot, packId);
        var packRootFull = Path.GetFullPath(packRoot);
        var packsRootFull = Path.GetFullPath(packsRoot);
        if (!packRootFull.StartsWith(packsRootFull, StringComparison.OrdinalIgnoreCase))
        {
            return Fail<PackDeleteResponseDto>("pack path invalid", statusCode: 400);
        }

        if (!Directory.Exists(packRoot))
        {
            return Fail<PackDeleteResponseDto>("pack not found", statusCode: 404);
        }

        var now = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(packVersion))
        {
            var deletedVersions = entry.Versions?.ToArray() ?? Array.Empty<string>();
            Directory.Delete(packRoot, recursive: true);
            index.Packs.Remove(entry);
            await SaveIndexAsync(packsRoot, index, cancellationToken).ConfigureAwait(false);

            await _auditRecorder.RecordDeleteAsync(new PackLifecycleAuditPayload
            {
                TenantId = request.TenantId,
                PackId = entry.PackId,
                ActorUserId = request.ActorUserId.Trim(),
                Reason = request.Reason,
                Action = "delete",
                DeletedVersions = deletedVersions,
                TimestampUtc = now
            }, cancellationToken).ConfigureAwait(false);

            return ServiceResponse<PackDeleteResponseDto>.Success(new PackDeleteResponseDto
            {
                PackId = entry.PackId,
                PackVersion = null,
                Status = "deleted",
                DeletedVersions = deletedVersions,
                RemainingVersions = Array.Empty<string>(),
                Latest = null,
                DeletedUtc = now
            });
        }

        var match = entry.Versions?.FirstOrDefault(v =>
            string.Equals(v, packVersion, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return Fail<PackDeleteResponseDto>("pack version not found", statusCode: 404);
        }

        var versionRoot = Path.Combine(packRoot, match);
        var versionRootFull = Path.GetFullPath(versionRoot);
        if (!versionRootFull.StartsWith(packRootFull, StringComparison.OrdinalIgnoreCase))
        {
            return Fail<PackDeleteResponseDto>("pack version path invalid", statusCode: 400);
        }

        if (!Directory.Exists(versionRoot))
        {
            return Fail<PackDeleteResponseDto>("pack version not found", statusCode: 404);
        }

        Directory.Delete(versionRoot, recursive: true);
        entry.Versions?.Remove(match);

        if (entry.Versions is null || entry.Versions.Count == 0)
        {
            if (Directory.Exists(packRoot))
            {
                Directory.Delete(packRoot, recursive: true);
            }

            index.Packs.Remove(entry);
            await SaveIndexAsync(packsRoot, index, cancellationToken).ConfigureAwait(false);

            await _auditRecorder.RecordDeleteAsync(new PackLifecycleAuditPayload
            {
                TenantId = request.TenantId,
                PackId = entry.PackId,
                PackVersion = match,
                ActorUserId = request.ActorUserId.Trim(),
                Reason = request.Reason,
                Action = "delete",
                DeletedVersions = new[] { match },
                TimestampUtc = now
            }, cancellationToken).ConfigureAwait(false);

            return ServiceResponse<PackDeleteResponseDto>.Success(new PackDeleteResponseDto
            {
                PackId = entry.PackId,
                PackVersion = match,
                Status = "deleted",
                DeletedVersions = new[] { match },
                RemainingVersions = Array.Empty<string>(),
                Latest = null,
                DeletedUtc = now
            });
        }

        var remaining = entry.Versions.ToArray();
        if (string.Equals(entry.Latest, match, StringComparison.OrdinalIgnoreCase))
        {
            entry.Latest = SelectLatestVersion(remaining);
        }

        var latestVersion = entry.Latest ?? SelectLatestVersion(remaining);
        if (!string.IsNullOrWhiteSpace(latestVersion))
        {
            entry.Latest = latestVersion;
            var pointerResult = await UpdatePackPointersAsync(packRoot, latestVersion, cancellationToken)
                .ConfigureAwait(false);
            if (!pointerResult.IsSuccess)
            {
                return ServiceResponse<PackDeleteResponseDto>.Fail(
                    pointerResult.Error ?? new DomainError(ErrorCodes.InvalidRequest, "pack pointer update failed"),
                    pointerResult.StatusCode,
                    pointerResult.Message ?? "pack pointer update failed");
            }
        }

        await SaveIndexAsync(packsRoot, index, cancellationToken).ConfigureAwait(false);

        await _auditRecorder.RecordDeleteAsync(new PackLifecycleAuditPayload
        {
            TenantId = request.TenantId,
            PackId = entry.PackId,
            PackVersion = match,
            ActorUserId = request.ActorUserId.Trim(),
            Reason = request.Reason,
            Action = "delete",
            DeletedVersions = new[] { match },
            Latest = entry.Latest,
            TimestampUtc = now
        }, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<PackDeleteResponseDto>.Success(new PackDeleteResponseDto
        {
            PackId = entry.PackId,
            PackVersion = match,
            Status = "deleted",
            DeletedVersions = new[] { match },
            RemainingVersions = remaining,
            Latest = entry.Latest,
            DeletedUtc = now
        });
    }

    private static ServiceResponse<T> Fail<T>(string message, int statusCode = 400)
    {
        return ServiceResponse<T>.Fail(
            new DomainError(ErrorCodes.InvalidRequest, message),
            statusCode: statusCode,
            message: message);
    }

    private static PackIndexEntry? FindEntry(PackIndex index, string packId)
    {
        return index.Packs.FirstOrDefault(entry =>
            string.Equals(entry.PackId, packId, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<ServiceResponse<bool>> UpdatePackPointersAsync(
        string packRoot,
        string latestVersion,
        CancellationToken cancellationToken)
    {
        var versionRoot = Path.Combine(packRoot, latestVersion);
        var packPath = Path.Combine(versionRoot, "pack.json");
        var anchorPath = Path.Combine(versionRoot, "anchor.txt");

        if (!File.Exists(packPath) || !File.Exists(anchorPath))
        {
            return Fail<bool>("latest pack metadata missing", statusCode: 500);
        }

        var packJson = await File.ReadAllTextAsync(packPath, cancellationToken).ConfigureAwait(false);
        var anchor = await File.ReadAllTextAsync(anchorPath, cancellationToken).ConfigureAwait(false);

        var rootPackPath = Path.Combine(packRoot, "pack.json");
        var rootAnchorPath = Path.Combine(packRoot, "anchor.txt");
        await File.WriteAllTextAsync(rootPackPath, packJson, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(rootAnchorPath, anchor, cancellationToken).ConfigureAwait(false);

        var latestRoot = Path.Combine(packRoot, "latest");
        Directory.CreateDirectory(latestRoot);
        var latestPackPath = Path.Combine(latestRoot, "pack.json");
        var latestAnchorPath = Path.Combine(latestRoot, "anchor.txt");
        await File.WriteAllTextAsync(latestPackPath, packJson, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(latestAnchorPath, anchor, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<bool>.Success(true);
    }

    private static async Task<ServiceResponse<PackIndex>> LoadIndexAsync(
        string packsRoot,
        CancellationToken cancellationToken)
    {
        var indexPath = Path.Combine(packsRoot, "index.json");
        if (!File.Exists(indexPath))
        {
            return Fail<PackIndex>("pack index not found", statusCode: 404);
        }

        try
        {
            var raw = await File.ReadAllTextAsync(indexPath, cancellationToken).ConfigureAwait(false);
            var index = JsonSerializer.Deserialize<PackIndex>(raw, JsonOptions) ?? new PackIndex();
            return ServiceResponse<PackIndex>.Success(index);
        }
        catch (JsonException)
        {
            return Fail<PackIndex>("pack index invalid", statusCode: 500);
        }
    }

    private static async Task SaveIndexAsync(
        string packsRoot,
        PackIndex index,
        CancellationToken cancellationToken)
    {
        var indexPath = Path.Combine(packsRoot, "index.json");
        var payload = JsonSerializer.Serialize(index, JsonOptions);
        await File.WriteAllTextAsync(indexPath, payload, cancellationToken).ConfigureAwait(false);
    }

    private static string? SelectLatestVersion(IEnumerable<string> versions)
    {
        var list = versions.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        list.Sort(CompareVersions);
        return list.LastOrDefault();
    }

    private static int CompareVersions(string? left, string? right)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        if (Version.TryParse(left, out var lv) && Version.TryParse(right, out var rv))
        {
            return lv.CompareTo(rv);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
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
