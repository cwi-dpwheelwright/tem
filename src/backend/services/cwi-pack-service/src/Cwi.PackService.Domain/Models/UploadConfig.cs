namespace Cwi.PackService.Domain.Models;

public sealed class UploadConfig
{
    public long MaxBytes { get; init; }
    public int MaxEntries { get; init; }

    public static UploadConfig FromEnvironment()
    {
        var maxBytes = GetLongFromEnv("PACK_UPLOAD_MAX_BYTES", 50 * 1024 * 1024);
        var maxEntries = GetIntFromEnv("PACK_UPLOAD_MAX_ENTRIES", 2000);
        return new UploadConfig { MaxBytes = maxBytes, MaxEntries = maxEntries };
    }

    private static long GetLongFromEnv(string name, long fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(raw) && long.TryParse(raw, out var value) ? value : fallback;
    }

    private static int GetIntFromEnv(string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var value) ? value : fallback;
    }
}
