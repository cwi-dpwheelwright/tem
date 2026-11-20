namespace Cwi.PackService.Domain.Models;

public sealed record PackFileDescriptor(string FilePath, string ContentType, string? AnchorPath, string? ETag);
