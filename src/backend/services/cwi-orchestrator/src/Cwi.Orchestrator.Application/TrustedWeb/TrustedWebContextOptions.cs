#nullable enable
namespace Cwi.Orchestrator.Application.TrustedWeb;

public sealed class TrustedWebContextOptions
{
    public bool Enabled { get; init; }
    public string ProfilePath { get; init; } = "/etc/cwi/trusted-web-context.profiles.json";
    public string DefaultProfile { get; init; } = "general";
    public int MaxResults { get; init; } = 5;
    public int FetchTimeoutMs { get; init; } = 5000;
    public int CacheTtlSeconds { get; init; } = 21600;
    public int MaxSnippetChars { get; init; } = 1200;
    public bool RespectRobots { get; init; } = true;
    public string UserAgent { get; init; } = "CWI-TrustedWebContext/1.0";
    public bool PaidProvidersEnabled { get; init; }

    public static TrustedWebContextOptions LoadFromEnvironment()
    {
        return new TrustedWebContextOptions
        {
            Enabled = ReadBool("CWI_TRUSTED_WEB_CONTEXT_ENABLED", false),
            ProfilePath = ReadString("CWI_TRUSTED_WEB_CONTEXT_PROFILE_PATH", "/etc/cwi/trusted-web-context.profiles.json"),
            DefaultProfile = ReadString("CWI_TRUSTED_WEB_CONTEXT_DEFAULT_PROFILE", "general"),
            MaxResults = ReadInt("CWI_TRUSTED_WEB_CONTEXT_MAX_RESULTS", 5, 1, 10),
            FetchTimeoutMs = ReadInt("CWI_TRUSTED_WEB_CONTEXT_FETCH_TIMEOUT_MS", 5000, 500, 30000),
            CacheTtlSeconds = ReadInt("CWI_TRUSTED_WEB_CONTEXT_CACHE_TTL_SECONDS", 21600, 0, 86400),
            MaxSnippetChars = ReadInt("CWI_TRUSTED_WEB_CONTEXT_MAX_SNIPPET_CHARS", 1200, 200, 4000),
            RespectRobots = ReadBool("CWI_TRUSTED_WEB_CONTEXT_RESPECT_ROBOTS", true),
            UserAgent = ReadString("CWI_TRUSTED_WEB_CONTEXT_USER_AGENT", "CWI-TrustedWebContext/1.0"),
            PaidProvidersEnabled = ReadBool("CWI_TRUSTED_WEB_CONTEXT_PAID_PROVIDERS_ENABLED", false)
        };
    }

    private static string ReadString(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static bool ReadBool(string name, bool fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim() switch
        {
            "1" => true,
            "0" => false,
            var raw when bool.TryParse(raw, out var parsed) => parsed,
            _ => fallback
        };
    }

    private static int ReadInt(string name, int fallback, int min, int max)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!int.TryParse(value, out var parsed))
        {
            return fallback;
        }

        return Math.Min(max, Math.Max(min, parsed));
    }
}
#nullable disable
