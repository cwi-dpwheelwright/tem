namespace Cwi.PackService.Domain.Storage;

public interface IPackRepository
{
    string PacksRoot { get; }

    string? ResolvePath(params string[] segments);
    bool FileExists(string path);
    bool DirectoryExists(string path);
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);
    Task WriteAllTextAtomicAsync(string path, string contents, CancellationToken cancellationToken);
    void EnsureDirectory(string path);
    void DeleteDirectory(string path);
    bool CanAccessDirectory();
    string? ComputeEtag(string? anchorPath, string filePath);

    Task<bool> IsZipBundleAsync(string path, CancellationToken cancellationToken);
    Task<string?> ReadPackJsonFromZipAsync(string path, CancellationToken cancellationToken);
    Task ExtractZipAsync(string path, string destinationRoot, Models.UploadConfig uploadConfig, CancellationToken cancellationToken);
}
