using System.Globalization;

namespace Cwi.Orchestrator.Application.Guardrails.Units;

public enum Dimension
{
    Unknown = 0,
    Mass,
    Length,
    Volume,
    Time,
    Temperature
}

public readonly record struct Quantity(double Value, string Unit);

public readonly record struct NormalizedQuantity(double Value, string Unit, Dimension Dimension);

public sealed class UnitNormalizationResult
{
    private UnitNormalizationResult(bool success, NormalizedQuantity? normalized, string? error)
    {
        Success = success;
        Normalized = normalized;
        Error = error;
    }

    public bool Success { get; }
    public NormalizedQuantity? Normalized { get; }
    public string? Error { get; }

    public static UnitNormalizationResult Ok(NormalizedQuantity normalized) => new(true, normalized, null);
    public static UnitNormalizationResult Fail(string error) => new(false, null, error);
}

public static class UnitNormalizer
{
    private sealed record UnitDefinition(string Symbol, Dimension Dimension, string BaseUnit, double Scale, double Offset);

    private static readonly IReadOnlyDictionary<string, UnitDefinition> Units = new Dictionary<string, UnitDefinition>(StringComparer.OrdinalIgnoreCase)
    {
        // Mass -> kg
        ["kg"] = new("kg", Dimension.Mass, "kg", 1.0, 0.0),
        ["g"] = new("g", Dimension.Mass, "kg", 1e-3, 0.0),
        ["mg"] = new("mg", Dimension.Mass, "kg", 1e-6, 0.0),
        ["ug"] = new("ug", Dimension.Mass, "kg", 1e-9, 0.0),

        // Length -> m
        ["m"] = new("m", Dimension.Length, "m", 1.0, 0.0),
        ["cm"] = new("cm", Dimension.Length, "m", 1e-2, 0.0),
        ["mm"] = new("mm", Dimension.Length, "m", 1e-3, 0.0),
        ["km"] = new("km", Dimension.Length, "m", 1e3, 0.0),
        ["in"] = new("in", Dimension.Length, "m", 0.0254, 0.0),
        ["ft"] = new("ft", Dimension.Length, "m", 0.3048, 0.0),

        // Volume -> l
        ["l"] = new("l", Dimension.Volume, "l", 1.0, 0.0),
        ["ml"] = new("ml", Dimension.Volume, "l", 1e-3, 0.0),
        ["m3"] = new("m3", Dimension.Volume, "l", 1e3, 0.0),

        // Time -> s
        ["s"] = new("s", Dimension.Time, "s", 1.0, 0.0),
        ["ms"] = new("ms", Dimension.Time, "s", 1e-3, 0.0),
        ["min"] = new("min", Dimension.Time, "s", 60.0, 0.0),
        ["h"] = new("h", Dimension.Time, "s", 3600.0, 0.0),

        // Temperature -> K
        ["k"] = new("K", Dimension.Temperature, "K", 1.0, 0.0),
        ["c"] = new("C", Dimension.Temperature, "K", 1.0, 273.15),
        ["f"] = new("F", Dimension.Temperature, "K", 5.0 / 9.0, 459.67)
    };

    public static bool TryParseQuantity(string? text, out Quantity quantity, out string? error)
    {
        quantity = default;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Quantity text is empty.";
            return false;
        }

        var trimmed = text.Trim();
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            error = "Quantity must include value and unit (e.g., \"10 mg\").";
            return false;
        }

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            error = $"Invalid numeric value '{parts[0]}'.";
            return false;
        }

        var unit = string.Join(' ', parts.Skip(1));
        if (string.IsNullOrWhiteSpace(unit))
        {
            error = "Unit is required.";
            return false;
        }

        quantity = new Quantity(value, unit.Trim());
        return true;
    }

    public static UnitNormalizationResult Normalize(Quantity quantity)
    {
        if (!Units.TryGetValue(quantity.Unit, out var definition))
        {
            return UnitNormalizationResult.Fail($"Unsupported unit '{quantity.Unit}'.");
        }

        var normalizedValue = (quantity.Value + definition.Offset) * definition.Scale;
        var normalized = new NormalizedQuantity(normalizedValue, definition.BaseUnit, definition.Dimension);
        return UnitNormalizationResult.Ok(normalized);
    }

    public static bool AreCompatible(Quantity left, Quantity right)
    {
        if (!Units.TryGetValue(left.Unit, out var leftDef))
        {
            return false;
        }

        if (!Units.TryGetValue(right.Unit, out var rightDef))
        {
            return false;
        }

        return leftDef.Dimension == rightDef.Dimension;
    }
}
