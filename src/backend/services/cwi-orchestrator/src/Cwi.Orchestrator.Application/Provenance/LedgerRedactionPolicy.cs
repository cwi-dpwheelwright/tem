using System.Text.Json;
using System.Linq;

namespace Cwi.Orchestrator.Application.Provenance;

public sealed class LedgerRedactionPolicy
{
    private const string RedactedValue = "[REDACTED]";

    public LedgerRedactionPolicy(bool enabled, IReadOnlyList<string> fields)
    {
        Enabled = enabled;
        Fields = fields;
    }

    public bool Enabled { get; }
    public IReadOnlyList<string> Fields { get; }

    public string RedactPayload(string payloadJson)
    {
        if (!Enabled || Fields.Count == 0 || string.IsNullOrWhiteSpace(payloadJson))
        {
            return payloadJson;
        }

        using var document = JsonDocument.Parse(payloadJson);
        var redacted = RedactElement(document.RootElement);
        return JsonSerializer.Serialize(redacted);
    }

    private object? RedactElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => RedactObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(RedactElement).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private Dictionary<string, object?> RedactObject(JsonElement element)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (Fields.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            {
                result[property.Name] = RedactedValue;
                continue;
            }

            result[property.Name] = RedactElement(property.Value);
        }

        return result;
    }
}
