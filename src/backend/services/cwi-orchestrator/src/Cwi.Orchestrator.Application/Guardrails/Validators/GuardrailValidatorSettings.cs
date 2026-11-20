using System.Globalization;

namespace Cwi.Orchestrator.Application.Guardrails.Validators;

public sealed class GuardrailValidatorSettings
{
    public GuardrailValidatorSettings(IReadOnlyDictionary<string, string>? values = null)
    {
        Values = values ?? new Dictionary<string, string>();
    }

    public IReadOnlyDictionary<string, string> Values { get; }

    public string? GetString(string key)
        => Values.TryGetValue(key, out var value) ? value : null;

    public double? GetDouble(string key)
    {
        if (!Values.TryGetValue(key, out var value))
        {
            return null;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    public int? GetInt(string key)
    {
        if (!Values.TryGetValue(key, out var value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
