using System.Text;
using System.Text.Json;

namespace Cwi.Orchestrator.Application.Provenance;

public static class CanonicalJson
{
    public static string Canonicalize(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => CanonicalizeObject(element),
            JsonValueKind.Array => CanonicalizeArray(element),
            JsonValueKind.String => JsonSerializer.Serialize(element.GetString()),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => element.GetRawText()
        };
    }

    private static string CanonicalizeObject(JsonElement element)
    {
        var props = element.EnumerateObject()
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        var builder = new StringBuilder();
        builder.Append('{');
        for (var i = 0; i < props.Count; i++)
        {
            var prop = props[i];
            builder.Append(JsonSerializer.Serialize(prop.Name));
            builder.Append(':');
            builder.Append(Canonicalize(prop.Value));
            if (i < props.Count - 1)
            {
                builder.Append(',');
            }
        }
        builder.Append('}');
        return builder.ToString();
    }

    private static string CanonicalizeArray(JsonElement element)
    {
        var items = element.EnumerateArray().ToList();
        var builder = new StringBuilder();
        builder.Append('[');
        for (var i = 0; i < items.Count; i++)
        {
            builder.Append(Canonicalize(items[i]));
            if (i < items.Count - 1)
            {
                builder.Append(',');
            }
        }
        builder.Append(']');
        return builder.ToString();
    }
}
