using System.Text.Json;

namespace Cwi.Orchestrator.Application.Reconciliation;

public sealed class RecordReconciler
{
    public Dictionary<string, object?> BuildCanonical(IEnumerable<SourceRecordDto> sources)
    {
        var ordered = sources
            .OrderByDescending(source => source.UpdatedUtc)
            .ToList();

        var canonical = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in ordered)
        {
            foreach (var entry in source.Attributes)
            {
                if (!canonical.ContainsKey(entry.Key))
                {
                    canonical[entry.Key] = entry.Value;
                }
            }
        }

        return canonical;
    }

    public static Dictionary<string, object?> NormalizeCanonical(Dictionary<string, object?> canonical)
    {
        return canonical
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(
                pair => pair.Key.Trim(),
                pair => NormalizeValue(pair.Value),
                StringComparer.OrdinalIgnoreCase);
    }

    private static object? NormalizeValue(object? value)
    {
        if (value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.ToString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }

        return value;
    }
}
