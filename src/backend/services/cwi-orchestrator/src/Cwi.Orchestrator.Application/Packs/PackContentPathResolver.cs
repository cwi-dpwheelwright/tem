using System.Text.Json;

namespace Cwi.Orchestrator.Application.Packs;

public static class PackContentPathResolver
{
    public static string ResolveVersionedRelativePath(string packName, string? packVersion, JsonElement root)
    {
        var packRef = string.IsNullOrWhiteSpace(packVersion) ? "latest" : packVersion.Trim();
        var contentPath = ResolveDeclaredOrConventionalRelativePath(packName, root);
        return $"{Uri.EscapeDataString(packRef)}/{EncodeRelativePath(contentPath)}";
    }

    public static string? ResolveLocalPath(string packRoot, string packName, JsonElement root)
    {
        var contentDirectory = Path.Combine(packRoot, "content");
        if (!Directory.Exists(contentDirectory))
        {
            return null;
        }

        var declaredPath = ResolveDeclaredRelativePath(root);
        if (!string.IsNullOrWhiteSpace(declaredPath))
        {
            var candidate = Path.Combine(packRoot, ToLocalRelativePath(declaredPath));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var conventionalCandidate = Path.Combine(packRoot, ToLocalRelativePath(ResolveConventionalRelativePath(packName)));
        if (File.Exists(conventionalCandidate))
        {
            return conventionalCandidate;
        }

        var candidates = Directory
            .EnumerateFiles(contentDirectory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var extension = Path.GetExtension(path);
                return string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return candidates.FirstOrDefault();
    }

    private static string ResolveDeclaredOrConventionalRelativePath(string packName, JsonElement root)
    {
        return ResolveDeclaredRelativePath(root) ?? ResolveConventionalRelativePath(packName);
    }

    private static string ResolveConventionalRelativePath(string packName)
    {
        return $"content/{packName.Trim().Replace('-', '_')}.txt";
    }

    private static string? ResolveDeclaredRelativePath(JsonElement root)
    {
        if (!root.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var module in modules.EnumerateArray())
        {
            if (module.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var path = TryGetString(module, "path");
            if (!string.IsNullOrWhiteSpace(path))
            {
                return NormalizeRelativePath(path);
            }
        }

        return null;
    }

    private static string EncodeRelativePath(string relativePath)
    {
        return string.Join('/',
            NormalizeRelativePath(relativePath)
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
    }

    private static string ToLocalRelativePath(string relativePath)
    {
        return NormalizeRelativePath(relativePath).Replace('/', Path.DirectorySeparatorChar);
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Trim().TrimStart('/', '\\').Replace('\\', '/');
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
