namespace Cwi.Orchestrator.Application.Ask;

public sealed class LanguageNormalizationOptions
{
    public LanguageNormalizationMode Mode { get; init; }

    public string? BaseUrl { get; init; }

    public int TimeoutMs { get; init; }

    public static LanguageNormalizationOptions LoadFromEnvironment()
    {
        return new LanguageNormalizationOptions
        {
            Mode = ParseMode(Environment.GetEnvironmentVariable("LANGUAGE_NORMALIZATION_MODE")),
            BaseUrl = NormalizeOptional(Environment.GetEnvironmentVariable("LANGUAGE_NORMALIZATION_BASE_URL")),
            TimeoutMs = ParseInt("LANGUAGE_NORMALIZATION_TIMEOUT_MS", defaultValue: 750)
        };
    }

    private static LanguageNormalizationMode ParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return LanguageNormalizationMode.Local;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "local" => LanguageNormalizationMode.Local,
            "external" => LanguageNormalizationMode.External,
            "external_with_local_fallback" or "fallback" => LanguageNormalizationMode.ExternalWithLocalFallback,
            _ => throw new InvalidOperationException("LANGUAGE_NORMALIZATION_MODE must be local, external, or external_with_local_fallback.")
        };
    }

    private static int ParseInt(string name, int defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        if (!int.TryParse(raw, out var value) || value <= 0)
        {
            throw new InvalidOperationException($"{name} must be a positive integer.");
        }

        return value;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public enum LanguageNormalizationMode
{
    Local,
    External,
    ExternalWithLocalFallback
}
