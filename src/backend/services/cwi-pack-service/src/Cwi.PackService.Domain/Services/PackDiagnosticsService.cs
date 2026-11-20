using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.PackService.Domain.Models;
using Cwi.PackService.Domain.Storage;

namespace Cwi.PackService.Domain.Services;

public sealed class PackDiagnosticsService
{
    private readonly IPackRepository _repository;

    public PackDiagnosticsService(IPackRepository repository)
    {
        _repository = repository;
    }

    public async Task<ServiceResponse<PackDiagnosticsReport>> GetDiagnosticsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var report = new PackDiagnosticsReport
            {
                PacksRoot = _repository.PacksRoot,
                PacksRootAccessible = _repository.CanAccessDirectory()
            };

            var indexPath = _repository.ResolvePath("index.json");
            if (indexPath is null)
            {
                return Fail<PackDiagnosticsReport>("invalid_path", "Invalid pack root.", 500);
            }

            report.IndexPath = indexPath;
            report.IndexPresent = _repository.FileExists(indexPath);

            var index = new PackIndex();
            if (report.IndexPresent)
            {
                try
                {
                    var rawIndex = await _repository.ReadAllTextAsync(indexPath, cancellationToken).ConfigureAwait(false);
                    index = JsonSerializer.Deserialize<PackIndex>(rawIndex, PackServiceState.IndexJsonOptions) ?? new PackIndex();
                }
                catch (JsonException)
                {
                    report.IndexLoadError = "index.json is invalid JSON.";
                }
            }
            else
            {
                report.IndexLoadError = "index.json is missing.";
            }

            var indexedById = index.Packs
                .Where(entry => !string.IsNullOrWhiteSpace(entry.PackId))
                .GroupBy(entry => entry.PackId.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            report.IndexedPackCount = indexedById.Count;

            var discoveredPackIds = DiscoverPackDirectories(report);
            report.DiscoveredPackCount = discoveredPackIds.Count;

            var allPackIds = indexedById.Keys
                .Concat(discoveredPackIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(packId => packId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var cache = new Dictionary<string, PackCheckResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var packId in allPackIds)
            {
                var evaluation = await EvaluatePackAsync(
                        packId,
                        cache,
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                        cancellationToken)
                    .ConfigureAwait(false);

                indexedById.TryGetValue(packId, out var indexEntry);
                report.Packs.Add(CreateDiagnosticsEntry(indexEntry, evaluation));
            }

            report.AvailablePackCount = report.Packs.Count(entry => string.Equals(entry.Status, "available", StringComparison.OrdinalIgnoreCase));
            report.FilteredPackCount = report.Packs.Count(entry => string.Equals(entry.Status, "filtered", StringComparison.OrdinalIgnoreCase));
            report.UnindexedPackCount = report.Packs.Count(entry => string.Equals(entry.Status, "unindexed", StringComparison.OrdinalIgnoreCase));

            return ServiceResponse<PackDiagnosticsReport>.Success(report, 200, "OK");
        }
        catch
        {
            return Fail<PackDiagnosticsReport>("diagnostics_failed", "Pack diagnostics failed.", 500);
        }
    }

    public async Task<ServiceResponse<PackAvailabilitySummary>> GetAvailabilitySummaryAsync(CancellationToken cancellationToken)
    {
        var diagnostics = await GetDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
        if (!diagnostics.IsSuccess || diagnostics.Data is null)
        {
            return Fail<PackAvailabilitySummary>(
                diagnostics.Error?.Code ?? "diagnostics_failed",
                diagnostics.Error?.Message ?? diagnostics.Message,
                diagnostics.StatusCode);
        }

        var summary = BuildAvailabilitySummary(diagnostics.Data);
        return ServiceResponse<PackAvailabilitySummary>.Success(summary, 200, "OK");
    }

    private List<string> DiscoverPackDirectories(PackDiagnosticsReport report)
    {
        try
        {
            if (!Directory.Exists(_repository.PacksRoot))
            {
                return new List<string>();
            }

            return Directory.EnumerateDirectories(_repository.PacksRoot)
                .Where(path => File.Exists(Path.Combine(path, "pack.json")) || File.Exists(Path.Combine(path, "anchor.txt")))
                .Select(path => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
                .Where(name => !string.IsNullOrWhiteSpace(name) && IsSafeSegment(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            report.DirectoryScanError = ex.Message;
            return new List<string>();
        }
    }

    private async Task<PackCheckResult> EvaluatePackAsync(
        string packId,
        IDictionary<string, PackCheckResult> cache,
        HashSet<string> chain,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(packId, out var cached))
        {
            return cached;
        }

        if (!IsSafeSegment(packId))
        {
            var invalid = new PackCheckResult
            {
                PackId = packId,
                DependencyGraphAvailable = false,
                Issues =
                {
                    CreateErrorIssue("invalid_pack_id", "Pack identifier is not URL-safe.")
                }
            };
            cache[packId] = invalid;
            return invalid;
        }

        if (!chain.Add(packId))
        {
            return new PackCheckResult
            {
                PackId = packId,
                DependencyGraphAvailable = true,
                SchemaValid = true
            };
        }

        try
        {
            var result = new PackCheckResult
            {
                PackId = packId
            };

            var packRoot = _repository.ResolvePath(packId);
            result.DirectoryPresent = packRoot is not null && _repository.DirectoryExists(packRoot);

            var rootPackPath = _repository.ResolvePath(packId, "pack.json");
            var rootAnchorPath = _repository.ResolvePath(packId, "anchor.txt");
            var latestPackPath = _repository.ResolvePath(packId, "latest", "pack.json");
            var latestAnchorPath = _repository.ResolvePath(packId, "latest", "anchor.txt");

            result.RootPackJsonExists = rootPackPath is not null && _repository.FileExists(rootPackPath);
            result.RootAnchorExists = rootAnchorPath is not null && _repository.FileExists(rootAnchorPath);
            result.LatestPackJsonExists = latestPackPath is not null && _repository.FileExists(latestPackPath);
            result.LatestAnchorExists = latestAnchorPath is not null && _repository.FileExists(latestAnchorPath);

            if (!result.DirectoryPresent)
            {
                result.Issues.Add(CreateErrorIssue("directory_missing", "Pack directory is missing from PACKS_DIR."));
                result.DependencyGraphAvailable = false;
                cache[packId] = result;
                return result;
            }

            if (!result.RootPackJsonExists)
            {
                result.Issues.Add(CreateErrorIssue("pack_json_missing", "Root pack.json is missing."));
                result.DependencyGraphAvailable = false;
                cache[packId] = result;
                return result;
            }

            if (!result.RootAnchorExists)
            {
                result.Issues.Add(CreateWarningIssue("anchor_missing", "Root anchor.txt is missing."));
            }

            if (!result.LatestPackJsonExists)
            {
                result.Issues.Add(CreateWarningIssue("latest_pack_json_missing", "latest/pack.json is missing."));
            }

            if (!result.LatestAnchorExists)
            {
                result.Issues.Add(CreateWarningIssue("latest_anchor_missing", "latest/anchor.txt is missing."));
            }

            string rawPack;
            try
            {
                rawPack = await _repository.ReadAllTextAsync(rootPackPath!, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result.Issues.Add(CreateErrorIssue("pack_read_failed", $"Root pack.json could not be read. {ex.Message}"));
                result.DependencyGraphAvailable = false;
                cache[packId] = result;
                return result;
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(rawPack);
            }
            catch (JsonException)
            {
                result.SchemaValid = false;
                result.Issues.Add(CreateErrorIssue("json_invalid", "Root pack.json is invalid JSON."));
                result.DependencyGraphAvailable = false;
                cache[packId] = result;
                return result;
            }

            var schemaResult = PackServiceState.SchemaValidator.Validate(node);
            result.SchemaValid = schemaResult.IsSuccess;
            if (!schemaResult.IsSuccess)
            {
                result.Issues.Add(CreateErrorIssue(
                    schemaResult.Error?.Code ?? "schema_invalid",
                    schemaResult.Error?.Message ?? schemaResult.Message));
                result.DependencyGraphAvailable = false;
                cache[packId] = result;
                return result;
            }

            using var document = JsonDocument.Parse(rawPack);
            result.Dependencies.AddRange(ReadDependencies(document.RootElement));

            foreach (var dependency in result.Dependencies.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var dependencyResult = await EvaluatePackAsync(dependency, cache, chain, cancellationToken).ConfigureAwait(false);
                if (dependencyResult.DependencyGraphAvailable)
                {
                    continue;
                }

                var firstBlockingIssue = dependencyResult.Issues.FirstOrDefault(issue =>
                    string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));
                var detail = firstBlockingIssue?.Message ?? "Dependency pack is unavailable.";
                result.Issues.Add(CreateErrorIssue(
                    "dependency_unavailable",
                    $"Dependency pack '{dependency}' is unavailable. {detail}",
                    dependency));
            }

            result.DependencyGraphAvailable = !result.Issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase));
            cache[packId] = result;
            return result;
        }
        finally
        {
            chain.Remove(packId);
        }
    }

    private static PackDiagnosticsEntry CreateDiagnosticsEntry(PackIndexEntry? indexEntry, PackCheckResult evaluation)
    {
        var issues = new List<PackDiagnosticsIssue>(evaluation.Issues);
        if (indexEntry is null)
        {
            issues.Add(CreateWarningIssue("index_entry_missing", "Pack directory exists on disk but no index.json entry references it."));
        }

        return new PackDiagnosticsEntry
        {
            PackId = evaluation.PackId,
            Name = indexEntry?.Name,
            Latest = indexEntry?.Latest,
            Visibility = indexEntry?.Visibility,
            Retired = indexEntry?.Retired,
            Status = indexEntry is null
                ? "unindexed"
                : evaluation.DependencyGraphAvailable
                    ? "available"
                    : "filtered",
            IndexEntryPresent = indexEntry is not null,
            DirectoryPresent = evaluation.DirectoryPresent,
            DependencyGraphAvailable = evaluation.DependencyGraphAvailable,
            SchemaValid = evaluation.SchemaValid,
            RootPackJsonExists = evaluation.RootPackJsonExists,
            RootAnchorExists = evaluation.RootAnchorExists,
            LatestPackJsonExists = evaluation.LatestPackJsonExists,
            LatestAnchorExists = evaluation.LatestAnchorExists,
            Dependencies = evaluation.Dependencies
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(dependency => dependency, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Issues = issues
        };
    }

    private static PackAvailabilitySummary BuildAvailabilitySummary(PackDiagnosticsReport report)
    {
        var summary = new PackAvailabilitySummary
        {
            GeneratedUtc = report.GeneratedUtc,
            Status = report.AvailablePackCount > 0 ? "available" : "empty",
            AvailablePackCount = report.AvailablePackCount,
            FilteredPackCount = report.FilteredPackCount,
            UnindexedPackCount = report.UnindexedPackCount,
            IndexedPackCount = report.IndexedPackCount,
            DiscoveredPackCount = report.DiscoveredPackCount
        };

        if (!report.IndexPresent)
        {
            if (report.DiscoveredPackCount > 0)
            {
                summary.Reasons.Add(CreateSummaryReason(
                    "index_missing",
                    $"{report.DiscoveredPackCount} pack{Pluralize(report.DiscoveredPackCount)} exist on disk, but index.json is missing.",
                    report.DiscoveredPackCount));
            }
            else if (report.IndexedPackCount == 0)
            {
                summary.Reasons.Add(CreateSummaryReason(
                    "no_packs_published",
                    "No packs are published for this site.",
                    null));
            }
        }
        else if (!string.IsNullOrWhiteSpace(report.IndexLoadError))
        {
            summary.Reasons.Add(CreateSummaryReason(
                "index_invalid",
                "Pack index metadata is present but unreadable.",
                null));
        }
        else if (report.IndexedPackCount == 0 && report.DiscoveredPackCount == 0)
        {
            summary.Reasons.Add(CreateSummaryReason(
                "no_packs_published",
                "No packs are published for this site.",
                null));
        }

        AddFilteredReason(
            summary,
            report,
            "missing_dependencies",
            new[] { "dependency_unavailable" },
            count => $"{count} indexed pack{Pluralize(count)} were filtered because required dependency packs are unavailable.");

        AddFilteredReason(
            summary,
            report,
            "invalid_pack_metadata",
            new[] { "schema_invalid", "json_invalid" },
            count => $"{count} indexed pack{Pluralize(count)} were filtered because pack metadata or schema validation failed.");

        AddFilteredReason(
            summary,
            report,
            "missing_pack_files",
            new[] { "directory_missing", "pack_json_missing", "pack_read_failed" },
            count => $"{count} indexed pack{Pluralize(count)} were filtered because required pack files are missing or unreadable.");

        if (report.UnindexedPackCount > 0)
        {
            summary.Reasons.Add(CreateSummaryReason(
                "unindexed_packs",
                $"{report.UnindexedPackCount} pack{Pluralize(report.UnindexedPackCount)} exist on disk but are not indexed for serving.",
                report.UnindexedPackCount));
        }

        if (summary.Reasons.Count == 0 && report.AvailablePackCount == 0)
        {
            summary.Reasons.Add(CreateSummaryReason(
                "no_dependency_verified_packs",
                "No dependency-verified packs are available right now.",
                null));
        }

        return summary;
    }

    private static void AddFilteredReason(
        PackAvailabilitySummary summary,
        PackDiagnosticsReport report,
        string code,
        IEnumerable<string> issueCodes,
        Func<int, string> formatMessage)
    {
        var count = report.Packs
            .Where(entry => string.Equals(entry.Status, "filtered", StringComparison.OrdinalIgnoreCase))
            .Where(entry => entry.Issues.Any(issue => issueCodes.Contains(issue.Code, StringComparer.OrdinalIgnoreCase)))
            .Select(entry => entry.PackId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (count > 0)
        {
            summary.Reasons.Add(CreateSummaryReason(code, formatMessage(count), count));
        }
    }

    private static PackAvailabilityReason CreateSummaryReason(string code, string message, int? count)
        => new()
        {
            Code = code,
            Message = message,
            Count = count
        };

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

    private static string Pluralize(int count) => count == 1 ? string.Empty : "s";

    private static PackDiagnosticsIssue CreateErrorIssue(string code, string message, string? dependencyPackId = null)
        => new()
        {
            Code = code,
            Severity = "error",
            Message = message,
            DependencyPackId = dependencyPackId
        };

    private static PackDiagnosticsIssue CreateWarningIssue(string code, string message, string? dependencyPackId = null)
        => new()
        {
            Code = code,
            Severity = "warning",
            Message = message,
            DependencyPackId = dependencyPackId
        };

    private static ServiceResponse<T> Fail<T>(string code, string message, int statusCode)
    {
        return ServiceResponse<T>.Fail(new DomainError(code, message), statusCode, message);
    }

    private sealed class PackCheckResult
    {
        public string PackId { get; set; } = string.Empty;
        public bool DirectoryPresent { get; set; }
        public bool RootPackJsonExists { get; set; }
        public bool RootAnchorExists { get; set; }
        public bool LatestPackJsonExists { get; set; }
        public bool LatestAnchorExists { get; set; }
        public bool DependencyGraphAvailable { get; set; }
        public bool? SchemaValid { get; set; }
        public List<string> Dependencies { get; } = new();
        public List<PackDiagnosticsIssue> Issues { get; } = new();
    }
}
