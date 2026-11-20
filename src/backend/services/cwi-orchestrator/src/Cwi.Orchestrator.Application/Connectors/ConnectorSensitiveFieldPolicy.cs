#nullable enable
using System.Text.Json;

namespace Cwi.Orchestrator.Application.Connectors;

public static class ConnectorSensitiveFieldPolicy
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "api_key",
        "apikey",
        "authorization",
        "password",
        "refresh_token",
        "secret",
        "token"
    };

    public static bool IsSensitiveKey(string key)
    {
        var normalized = key.Trim().ToLowerInvariant();
        return SensitiveKeys.Contains(normalized)
            || normalized.Contains("private_key", StringComparison.Ordinal)
            || normalized.Contains("client_secret", StringComparison.Ordinal)
            || normalized.EndsWith("_token", StringComparison.Ordinal)
            || normalized.EndsWith("_secret", StringComparison.Ordinal)
            || normalized.EndsWith("_password", StringComparison.Ordinal);
    }

    public static object? Redact(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                property => property.Name,
                property => IsSensitiveKey(property.Name) ? "[redacted]" : Redact(property.Value),
                StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(Redact).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => null
        };
    }
}
#nullable disable
