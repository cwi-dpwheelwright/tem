using System.Text.Json;

namespace Cwi.Orchestrator.Application.Reconciliation;

public sealed class ConflictDetector
{
    public List<ConflictEntryDto> DetectConflicts(IEnumerable<SourceRecordDto> sources)
    {
        var conflicts = new List<ConflictEntryDto>();
        var valuesByField = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            foreach (var entry in source.Attributes)
            {
                var normalized = NormalizeValue(entry.Value);
                if (!valuesByField.TryGetValue(entry.Key, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    valuesByField[entry.Key] = set;
                }
                set.Add(normalized);
            }
        }

        foreach (var pair in valuesByField)
        {
            if (pair.Value.Count <= 1)
            {
                continue;
            }

            conflicts.Add(new ConflictEntryDto
            {
                Field = pair.Key,
                Values = pair.Value.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList()
            });
        }

        return conflicts;
    }

    private static string NormalizeValue(object? value)
    {
        if (value is null)
        {
            return "<null>";
        }

        return value switch
        {
            string text => text.Trim(),
            JsonElement element => element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.ToString(),
            _ => value.ToString() ?? string.Empty
        };
    }
}
