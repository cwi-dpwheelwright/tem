using System.Globalization;
using System.Text.Json;
using Cwi.Orchestrator.Application.Guardrails.Parsing;
using Cwi.Orchestrator.Application.Guardrails.Scoring;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Guardrails.Governance;

public sealed class GuardrailCorrectionEngine
{
    private static readonly Dictionary<string, int> SeverityRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["none"] = 0,
        ["low"] = 1,
        ["medium"] = 2,
        ["high"] = 3,
        ["critical"] = 4
    };

    private readonly GuardrailCorrectionOptions _options;

    public GuardrailCorrectionEngine(GuardrailCorrectionOptions options)
    {
        _options = options ?? new GuardrailCorrectionOptions();
    }

    public GuardrailCorrection Apply(
        IReadOnlyDictionary<string, object?> payload,
        IReadOnlyList<GuardrailViolation> violations,
        SeverityScore score)
    {
        if (violations.Count == 0)
        {
            return new GuardrailCorrection { Applied = false };
        }

        if (!_options.Enabled || string.Equals(_options.Mode, "off", StringComparison.OrdinalIgnoreCase))
        {
            return new GuardrailCorrection
            {
                Applied = false,
                Mode = "disabled",
                Summary = "Guardrail correction disabled."
            };
        }

        if (ShouldQuarantine(score.Classification))
        {
            return new GuardrailCorrection
            {
                Applied = false,
                Mode = "quarantine",
                Summary = "Guardrail violations quarantined based on correction policy."
            };
        }

        var corrected = new Dictionary<string, object?>(payload, StringComparer.OrdinalIgnoreCase);
        var applied = false;
        var summaries = new List<string>();
        var corrections = new List<CorrectionDiff>();

        foreach (var violation in violations)
        {
            if (!string.Equals(violation.Id, "quantity_bounds", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var entry in payload)
            {
                var text = ExtractText(entry.Value);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var quantities = QuantityExtractor.Extract(text);
                if (quantities.Count == 0)
                {
                    continue;
                }

                var adjusted = text;
                foreach (var quantity in quantities)
                {
                    if (quantity.Value <= 0)
                    {
                        continue;
                    }

                    var safeValue = Math.Min(quantity.Value, 1000);
                    if (Math.Abs(safeValue - quantity.Value) < 0.0001)
                    {
                        continue;
                    }

                    var originalValue = quantity.Value.ToString("0.###", CultureInfo.InvariantCulture);
                    var safeValueText = safeValue.ToString("0.###", CultureInfo.InvariantCulture);
                    adjusted = adjusted.Replace(originalValue, safeValueText);
                    applied = true;
                    summaries.Add($"Adjusted '{originalValue} {quantity.Unit}' to '{safeValueText} {quantity.Unit}'.");
                    corrections.Add(new CorrectionDiff
                    {
                        Field = entry.Key,
                        From = $"{originalValue} {quantity.Unit}",
                        To = $"{safeValueText} {quantity.Unit}",
                        Note = "Adjusted to bounds-safe value."
                    });
                }

                if (applied)
                {
                    corrected[entry.Key] = adjusted;
                }
            }
        }

        return new GuardrailCorrection
        {
            Applied = applied,
            Mode = applied ? "corrected" : "no_op",
            Summary = applied ? string.Join(" ", summaries) : "No corrections applied.",
            CorrectedPayload = applied ? corrected : null,
            Corrections = corrections
        };
    }

    private static string? ExtractText(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => null
        };
    }

    private bool ShouldQuarantine(string? classification)
    {
        if (string.Equals(_options.Mode, "quarantine", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rank = ResolveRank(classification);
        var max = ResolveRank(_options.MaxSeverity);
        return rank > max;
    }

    private static int ResolveRank(string? classification)
    {
        if (string.IsNullOrWhiteSpace(classification))
        {
            return 0;
        }

        return SeverityRank.TryGetValue(classification.Trim(), out var rank) ? rank : 0;
    }
}
