using System.Collections.Concurrent;
using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Microsoft.Extensions.Logging;

namespace Cwi.Orchestrator.Application.Packs;

public interface ICorePackRuntimeCatalog
{
    bool Enabled { get; }
    IReadOnlyList<string> ManagedPackNames { get; }
    bool IsManaged(string packName);
    bool TryGetCurrent(string packName, out CorePackRuntimeEntry? entry);
    Task<ServiceResponse<CorePackRuntimeEntry>> GetAsync(string packName, CancellationToken cancellationToken);
    Task RefreshAllAsync(string reason, CancellationToken cancellationToken);
}

public sealed class CorePackRuntimeCatalog : ICorePackRuntimeCatalog, IDisposable
{
    private static readonly IReadOnlySet<string> AnchorExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "anchor",
        "anchor_hash",
        "pack_hash"
    };

    private readonly ApplicationOptions _options;
    private readonly ILogger<CorePackRuntimeCatalog> _logger;
    private readonly IReadOnlyDictionary<string, string> _packRefs;
    private readonly ConcurrentDictionary<string, CorePackRuntimeEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastCheckedUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _dirtyPacks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _sync = new(1, 1);
    private volatile bool _watchersStarted;
    private bool _disposed;

    public CorePackRuntimeCatalog(
        ApplicationOptions options,
        ILogger<CorePackRuntimeCatalog> logger)
    {
        _options = options;
        _logger = logger;
        _packRefs = BuildPackRefs(options);
    }

    public bool Enabled =>
        _packRefs.Count > 0
        && !string.IsNullOrWhiteSpace(_options.PacksDirectory)
        && Directory.Exists(_options.PacksDirectory)
        && _options.CorePackRefreshMode != CorePackRefreshMode.Off;

    public IReadOnlyList<string> ManagedPackNames => _packRefs.Keys.ToArray();

    public bool IsManaged(string packName)
    {
        return !string.IsNullOrWhiteSpace(packName) && _packRefs.ContainsKey(packName);
    }

    public bool TryGetCurrent(string packName, out CorePackRuntimeEntry? entry)
    {
        if (_entries.TryGetValue(packName, out var current))
        {
            entry = current;
            return true;
        }

        entry = null;
        return false;
    }

    public async Task<ServiceResponse<CorePackRuntimeEntry>> GetAsync(string packName, CancellationToken cancellationToken)
    {
        if (!IsManaged(packName))
        {
            return ServiceResponse<CorePackRuntimeEntry>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Pack is not managed by the core runtime catalog."),
                statusCode: 400,
                message: "Pack is not managed by the core runtime catalog");
        }

        await EnsureFreshAsync(packName, reason: "request", force: false, cancellationToken).ConfigureAwait(false);
        if (_entries.TryGetValue(packName, out var entry))
        {
            return ServiceResponse<CorePackRuntimeEntry>.Success(entry);
        }

        return ServiceResponse<CorePackRuntimeEntry>.Fail(
            new DomainError(ErrorCodes.DependencyUnavailable, "Managed core pack unavailable."),
            statusCode: 503,
            message: "Managed core pack unavailable");
    }

    public async Task RefreshAllAsync(string reason, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return;
        }

        EnsureWatchersStarted();
        foreach (var packName in _packRefs.Keys)
        {
            await EnsureFreshAsync(packName, reason, force: true, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var watcher in _watchers.Values)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
        _sync.Dispose();
    }

    private async Task EnsureFreshAsync(string packName, string reason, bool force, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return;
        }

        EnsureWatchersStarted();
        var now = DateTimeOffset.UtcNow;
        if (!force && !NeedsRefresh(packName, now))
        {
            return;
        }

        await _sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (!force && !NeedsRefresh(packName, now))
            {
                return;
            }

            _lastCheckedUtc[packName] = now;
            var current = _entries.TryGetValue(packName, out var existing) ? existing : null;
            var loadResult = TryLoadEntry(packName, current);
            if (loadResult.Entry is not null)
            {
                _entries[packName] = loadResult.Entry;
                _dirtyPacks.TryRemove(packName, out _);

                if (current is null)
                {
                    LogLoaded(packName, loadResult.Entry, reason, reloadStatus: "loaded");
                    return;
                }

                if (!string.Equals(current.Document.Anchor, loadResult.Entry.Document.Anchor, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(current.Document.Hash, loadResult.Entry.Document.Hash, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(current.Document.Version, loadResult.Entry.Document.Version, StringComparison.OrdinalIgnoreCase))
                {
                    LogReloaded(packName, current, loadResult.Entry, reason, reloadStatus: "reloaded");
                }

                return;
            }

            LogRefreshFailure(packName, current, loadResult.ErrorMessage ?? "Unknown load failure.", reason);
        }
        finally
        {
            _sync.Release();
        }
    }

    private bool NeedsRefresh(string packName, DateTimeOffset now)
    {
        if (!_entries.ContainsKey(packName))
        {
            return true;
        }

        if (_dirtyPacks.ContainsKey(packName))
        {
            return true;
        }

        if (!_lastCheckedUtc.TryGetValue(packName, out var lastChecked))
        {
            return true;
        }

        return now - lastChecked >= TimeSpan.FromSeconds(_options.CorePackRefreshIntervalSeconds);
    }

    private (CorePackRuntimeEntry? Entry, string? ErrorMessage) TryLoadEntry(string packName, CorePackRuntimeEntry? current)
    {
        if (string.IsNullOrWhiteSpace(_options.PacksDirectory))
        {
            return (null, "PACKS_DIR is not configured.");
        }

        var packRef = ResolvePackRef(packName);
        var packRoot = Path.Combine(_options.PacksDirectory, packName, packRef);
        var anchorPath = Path.Combine(packRoot, "anchor.txt");
        var manifestPath = Path.Combine(packRoot, "pack.json");
        if (!File.Exists(anchorPath))
        {
            return (null, $"Anchor file missing at {anchorPath}.");
        }

        if (!File.Exists(manifestPath))
        {
            return (null, $"Manifest file missing at {manifestPath}.");
        }

        var anchor = File.ReadAllText(anchorPath).Trim();
        if (string.IsNullOrWhiteSpace(anchor))
        {
            return (null, $"Anchor file is empty at {anchorPath}.");
        }

        var packJson = File.ReadAllText(manifestPath);
        JsonDocument manifest;
        try
        {
            manifest = JsonDocument.Parse(packJson);
        }
        catch (JsonException ex)
        {
            return (null, $"Manifest JSON invalid at {manifestPath}: {ex.Message}");
        }

        using var manifestScope = manifest;
        var root = manifest.RootElement;
        var computed = ComputeVerifiedHash(root, anchor);
        var resolvedHash = computed ?? anchor;
        if (computed is null)
        {
            _logger.LogWarning(
                "Managed core pack {PackId} anchor could not be recomputed locally. Using teacher-issued anchor {Anchor} from {AnchorPath}.",
                packName,
                anchor,
                anchorPath);
        }

        var metadata = PackManifestReader.Read(root);
        var resolvedName = metadata.DeclaredName ?? packName;
        var version = ResolveVersion(root) ?? packRef;
        var contentPath = PackContentPathResolver.ResolveLocalPath(packRoot, packName, root);
        if (contentPath is null || !File.Exists(contentPath))
        {
            return (null, $"Content file missing under {Path.Combine(packRoot, "content")}.");
        }

        var content = NormalizeContent(File.ReadAllText(contentPath), _options.BaselinePackContentMaxChars);
        if (string.IsNullOrWhiteSpace(content))
        {
            return (null, $"Content file is empty at {contentPath}.");
        }

        var artifactLocations = new[]
        {
            manifestPath,
            anchorPath,
            contentPath
        };

        var descriptor = new PackDescriptor(
            requestedName: packName,
            resolvedName: resolvedName,
            version: version,
            hash: resolvedHash,
            anchor: anchor,
            contractType: PackContractType.Legacy,
            artifactLocations: artifactLocations,
            entries: metadata.Entries,
            modules: metadata.Modules,
            packTarget: resolvedName,
            publicationSlug: resolvedName,
            publicationVersion: version,
            publicationPublishedFile: manifestPath,
            publicationLatestFile: contentPath);

        var document = new PackDocument(
            resolvedName,
            version,
            packJson,
            anchor,
            resolvedHash,
            PackContractType.Legacy,
            descriptor);

        var guidance = new BaselinePackGuidance(
            document.Name,
            document.Version,
            document.Hash,
            content,
            packFamily: metadata.PackFamily,
            moduleRole: metadata.ModuleRoles.FirstOrDefault(),
            applyStage: metadata.ApplyStage,
            hookName: metadata.HookName,
            alwaysLoad: metadata.AlwaysLoad,
            runtimeRequired: metadata.RuntimeRequired,
            forceMatchQueries: metadata.ForceMatchQueries,
            confidenceFloor: metadata.ConfidenceFloor);

        var loadedAtUtc = DateTimeOffset.UtcNow;
        var entry = new CorePackRuntimeEntry(
            document,
            guidance,
            packRef,
            packRoot,
            anchorPath,
            contentPath,
            loadedAtUtc);

        return (entry, null);
    }

    private void EnsureWatchersStarted()
    {
        if (_watchersStarted || !Enabled || _options.CorePackRefreshMode != CorePackRefreshMode.Watch)
        {
            return;
        }

        lock (_watchers)
        {
            if (_watchersStarted)
            {
                return;
            }

            foreach (var packName in _packRefs.Keys)
            {
                if (string.IsNullOrWhiteSpace(_options.PacksDirectory))
                {
                    continue;
                }

                var packRef = ResolvePackRef(packName);
                var packRoot = Path.Combine(_options.PacksDirectory, packName, packRef);
                if (!Directory.Exists(packRoot))
                {
                    continue;
                }

                var watcher = new FileSystemWatcher(packRoot, "anchor.txt")
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
                };

                watcher.Changed += (_, _) => MarkDirty(packName, reason: "changed");
                watcher.Created += (_, _) => MarkDirty(packName, reason: "created");
                watcher.Renamed += (_, _) => MarkDirty(packName, reason: "renamed");
                watcher.Deleted += (_, _) => MarkDirty(packName, reason: "deleted");
                watcher.EnableRaisingEvents = true;
                _watchers[packName] = watcher;
            }

            _watchersStarted = true;
        }
    }

    private void MarkDirty(string packName, string reason)
    {
        _dirtyPacks[packName] = 1;
        _logger.LogInformation(
            "PACK_REFRESH_DIRTY PackId={PackId} TimestampUtc={TimestampUtc:o} Reason={Reason}",
            packName,
            DateTimeOffset.UtcNow,
            reason);
    }

    private void LogLoaded(string packName, CorePackRuntimeEntry entry, string reason, string reloadStatus)
    {
        _logger.LogInformation(
            "PACK_RELOADED PackId={PackId} PackVersion={PackVersion} PackRef={PackRef} OldAnchor={OldAnchor} NewAnchor={NewAnchor} TimestampUtc={TimestampUtc:o} ReloadStatus={ReloadStatus} Reason={Reason}",
            packName,
            entry.Document.Version,
            entry.PackRef,
            "none",
            entry.Document.Anchor,
            entry.LoadedAtUtc,
            reloadStatus,
            reason);
    }

    private void LogReloaded(string packName, CorePackRuntimeEntry previous, CorePackRuntimeEntry current, string reason, string reloadStatus)
    {
        _logger.LogInformation(
            "PACK_RELOADED PackId={PackId} PackVersion={PackVersion} PackRef={PackRef} OldAnchor={OldAnchor} NewAnchor={NewAnchor} TimestampUtc={TimestampUtc:o} ReloadStatus={ReloadStatus} Reason={Reason}",
            packName,
            current.Document.Version,
            current.PackRef,
            previous.Document.Anchor,
            current.Document.Anchor,
            current.LoadedAtUtc,
            reloadStatus,
            reason);
    }

    private void LogRefreshFailure(string packName, CorePackRuntimeEntry? current, string errorMessage, string reason)
    {
        _logger.LogWarning(
            "PACK_RELOAD_FAILED PackId={PackId} PackVersion={PackVersion} PackRef={PackRef} OldAnchor={OldAnchor} NewAnchor={NewAnchor} TimestampUtc={TimestampUtc:o} ReloadStatus={ReloadStatus} Reason={Reason} Error={Error}",
            packName,
            current?.Document.Version ?? "unknown",
            ResolvePackRef(packName),
            current?.Document.Anchor ?? "none",
            current?.Document.Anchor ?? "none",
            DateTimeOffset.UtcNow,
            current is null ? "failed_no_snapshot" : "retained_previous_snapshot",
            reason,
            errorMessage);
    }

    private string ResolvePackRef(string packName)
    {
        return _packRefs.TryGetValue(packName, out var packRef) ? packRef : "latest";
    }

    private static string? ResolveVersion(JsonElement root)
    {
        var version = TryGetString(root, "pack_version")
            ?? TryGetString(root, "version");
        if (!string.IsNullOrWhiteSpace(version))
        {
            return version.Trim();
        }

        if (root.TryGetProperty("publication", out var publication) && publication.ValueKind == JsonValueKind.Object)
        {
            version = TryGetString(publication, "version");
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version.Trim();
            }
        }

        return null;
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

    private static string? ComputeVerifiedHash(JsonElement root, string anchor)
    {
        var computed = PackAnchor.ComputeHash(root);
        if (string.Equals(computed, anchor, StringComparison.OrdinalIgnoreCase))
        {
            return computed;
        }

        if (HasAnchorField(root))
        {
            var stripped = PackAnchor.ComputeHash(root, AnchorExclusions);
            if (string.Equals(stripped, anchor, StringComparison.OrdinalIgnoreCase))
            {
                return stripped;
            }
        }

        return null;
    }

    private static bool HasAnchorField(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object
               && (root.TryGetProperty("anchor", out _)
                   || root.TryGetProperty("anchor_hash", out _)
                   || root.TryGetProperty("pack_hash", out _));
    }

    private static IReadOnlyDictionary<string, string> BuildPackRefs(ApplicationOptions options)
    {
        var refs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packName in options.BaselinePackNames)
        {
            if (string.IsNullOrWhiteSpace(packName))
            {
                continue;
            }

            refs[packName.Trim()] = ResolveDefaultRef(packName, options);
        }

        return refs;
    }

    private static string ResolveDefaultRef(string packName, ApplicationOptions options)
    {
        if (string.Equals(packName, "cwi-communication-core", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(options.CoreCommunicationPackRef)
                ? "latest"
                : options.CoreCommunicationPackRef!;
        }

        if (string.Equals(packName, "cwi-identity", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(options.CoreIdentityPackRef)
                ? "latest"
                : options.CoreIdentityPackRef!;
        }

        return "latest";
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
