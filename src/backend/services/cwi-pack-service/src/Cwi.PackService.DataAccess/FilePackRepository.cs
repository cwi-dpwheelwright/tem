using System.IO.Compression;
using Cwi.PackService.Domain;
using Cwi.PackService.Domain.Models;
using Cwi.PackService.Domain.Storage;

namespace Cwi.PackService.DataAccess;

public sealed class FilePackRepository : IPackRepository
{
    public string PacksRoot { get; }

    public FilePackRepository(string packsRoot)
    {
        if (string.IsNullOrWhiteSpace(packsRoot))
        {
            throw new ArgumentException("packsRoot is required.", nameof(packsRoot));
        }

        var full = Path.GetFullPath(packsRoot);
        PacksRoot = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    public string? ResolvePath(params string[] segments)
    {
        if (segments is null || segments.Length == 0)
        {
            return null;
        }

        var combined = Path.Combine(new[] { PacksRoot }.Concat(segments).ToArray());
        var full = Path.GetFullPath(combined);

        return full.StartsWith(PacksRoot, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken) =>
        File.ReadAllTextAsync(path, cancellationToken);

    public async Task WriteAllTextAtomicAsync(string path, string contents, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".tmp-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(tempPath, contents, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
    }

    public void EnsureDirectory(string path) => Directory.CreateDirectory(path);

    public void DeleteDirectory(string path) => Directory.Delete(path, recursive: true);

    public bool CanAccessDirectory()
    {
        try
        {
            _ = Directory.EnumerateFileSystemEntries(PacksRoot).Take(1).ToList();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string? ComputeEtag(string? anchorPath, string filePath)
    {
        string? anchorValue = null;
        if (!string.IsNullOrWhiteSpace(anchorPath) && File.Exists(anchorPath))
        {
            anchorValue = File.ReadAllText(anchorPath).Trim();
        }

        if (!string.IsNullOrWhiteSpace(anchorValue))
        {
            return $"\"{anchorValue}\"";
        }

        var info = new FileInfo(filePath);
        var fallback = $"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}";
        return $"\"{fallback}\"";
    }

    public async Task<bool> IsZipBundleAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        var header = new byte[4];
        var read = await stream.ReadAsync(header, cancellationToken).ConfigureAwait(false);
        return read == 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04;
    }

    public async Task<string?> ReadPackJsonFromZipAsync(string path, CancellationToken cancellationToken)
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
        using var reader = new StreamReader(entryStream);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ExtractZipAsync(string path, string destinationRoot, UploadConfig uploadConfig, CancellationToken cancellationToken)
    {
        var rootFullPath = Path.GetFullPath(destinationRoot);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        long totalBytes = 0;
        var entryCount = 0;

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

            if (string.Equals(entry.Name, "pack.json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.Name, "anchor.txt", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entryCount++;
            if (entryCount > uploadConfig.MaxEntries)
            {
                throw new InvalidDataException("Too many files in bundle.");
            }

            if (entry.Length > 0)
            {
                totalBytes += entry.Length;
                if (totalBytes > uploadConfig.MaxBytes)
                {
                    throw new PayloadTooLargeException();
                }
            }

            var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!destinationPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Invalid entry path.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var entryStream = entry.Open();
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
            await entryStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
        }
    }
}
