namespace Cwi.Orchestrator.Application.Guardrails.Scoring;

public sealed class SeverityConfig
{
    public IReadOnlyDictionary<string, double> Weights { get; init; } = new Dictionary<string, double>
    {
        ["low"] = 0.1,
        ["medium"] = 0.4,
        ["high"] = 0.7,
        ["critical"] = 1.0
    };

    public IReadOnlyDictionary<string, double> Thresholds { get; init; } = new Dictionary<string, double>
    {
        ["low"] = 0.2,
        ["medium"] = 0.5,
        ["high"] = 0.8
    };

    public double DefaultWeight { get; init; } = 0.4;
}
