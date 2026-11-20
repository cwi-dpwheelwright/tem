using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Cwi.Orchestrator.Application.Packs;

public static class PackAnchor
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ComputeHash(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ComputeHash(document.RootElement);
    }

    public static string ComputeRawHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ComputeHash(JsonElement element, IReadOnlySet<string>? rootExclusions = null)
    {
        var canonical = Canonicalize(element, rootExclusions, isRoot: true);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Canonicalize(JsonElement element, IReadOnlySet<string>? rootExclusions = null)
    {
        return Canonicalize(element, rootExclusions, isRoot: true);
    }

    private static string Canonicalize(JsonElement element, IReadOnlySet<string>? rootExclusions, bool isRoot)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => CanonicalizeObject(element, rootExclusions, isRoot),
            JsonValueKind.Array => CanonicalizeArray(element, rootExclusions),
            JsonValueKind.String => JsonSerializer.Serialize(element.GetString(), CanonicalJsonOptions),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => element.GetRawText()
        };
    }

    private static string CanonicalizeObject(JsonElement element, IReadOnlySet<string>? rootExclusions, bool isRoot)
    {
        var props = element.EnumerateObject()
            .Where(p => !(isRoot && rootExclusions is not null && rootExclusions.Contains(p.Name)))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        var builder = new StringBuilder();
        builder.Append('{');
        for (var i = 0; i < props.Count; i++)
        {
            var prop = props[i];
            builder.Append(JsonSerializer.Serialize(prop.Name, CanonicalJsonOptions));
            builder.Append(':');
            builder.Append(Canonicalize(prop.Value, rootExclusions, isRoot: false));
            if (i < props.Count - 1)
            {
                builder.Append(',');
            }
        }
        builder.Append('}');
        return builder.ToString();
    }

    private static string CanonicalizeArray(JsonElement element, IReadOnlySet<string>? rootExclusions)
    {
        var items = element.EnumerateArray().ToList();
        var builder = new StringBuilder();
        builder.Append('[');
        for (var i = 0; i < items.Count; i++)
        {
            builder.Append(Canonicalize(items[i], rootExclusions, isRoot: false));
            if (i < items.Count - 1)
            {
                builder.Append(',');
            }
        }
        builder.Append(']');
        return builder.ToString();
    }
}
