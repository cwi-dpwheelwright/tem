namespace Cwi.PackService.Domain.Models;

public sealed record PackUploadRequest(
    string PackJson,
    string? BundlePath,
    bool IsZip,
    string? Notes,
    bool DryRun,
    string? PackIdOverride,
    string? PackVersionOverride);
