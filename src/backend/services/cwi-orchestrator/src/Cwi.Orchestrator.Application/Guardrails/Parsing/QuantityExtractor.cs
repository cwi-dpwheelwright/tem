using System.Globalization;
using System.Text.RegularExpressions;
using Cwi.Orchestrator.Application.Guardrails.Units;

namespace Cwi.Orchestrator.Application.Guardrails.Parsing;

public static partial class QuantityExtractor
{
    private static readonly HashSet<string> SupportedUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "kg", "g", "mg", "ug",
        "m", "cm", "mm", "km", "in", "ft",
        "l", "ml", "m3",
        "s", "ms", "min", "h",
        "k", "c", "f"
    };

    public static IReadOnlyList<Quantity> Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<Quantity>();
        }

        var matches = QuantityRegex().Matches(text);
        if (matches.Count == 0)
        {
            return Array.Empty<Quantity>();
        }

        var results = new List<Quantity>();
        foreach (Match match in matches)
        {
            var valueGroup = match.Groups["value"].Value;
            var unitGroup = match.Groups["unit"].Value;
            if (!SupportedUnits.Contains(unitGroup))
            {
                continue;
            }

            if (!double.TryParse(valueGroup, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            results.Add(new Quantity(value, unitGroup));
        }

        return results;
    }

    [GeneratedRegex(@"(?<value>-?\d+(?:\.\d+)?)\s*(?<unit>[a-zA-Z]+)")]
    private static partial Regex QuantityRegex();
}
