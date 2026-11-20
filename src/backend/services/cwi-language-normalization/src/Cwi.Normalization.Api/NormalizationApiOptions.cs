namespace Cwi.Normalization.Api;

public sealed class NormalizationApiOptions
{
    public int HttpPort { get; init; }

    public bool DevOpenApiEnabled { get; init; }

    public static NormalizationApiOptions LoadFromEnvironment()
    {
        return new NormalizationApiOptions
        {
            HttpPort = ParseInt(
                "LANGUAGE_NORMALIZATION_HTTP_PORT",
                "NORMALIZATION_HTTP_PORT",
                defaultValue: 8081),
            DevOpenApiEnabled = ParseBool(
                "LANGUAGE_NORMALIZATION_DEV_OPENAPI_ENABLED",
                "NORMALIZATION_DEV_OPENAPI_ENABLED",
                defaultValue: true)
        };
    }

    private static int ParseInt(string primaryName, string fallbackName, int defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(primaryName);
        var name = primaryName;
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = Environment.GetEnvironmentVariable(fallbackName);
            name = fallbackName;
        }

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

    private static bool ParseBool(string primaryName, string fallbackName, bool defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(primaryName);
        var name = primaryName;
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = Environment.GetEnvironmentVariable(fallbackName);
            name = fallbackName;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" => true,
            "0" or "false" or "no" => false,
            _ => throw new InvalidOperationException($"{name} must be true or false.")
        };
    }
}
