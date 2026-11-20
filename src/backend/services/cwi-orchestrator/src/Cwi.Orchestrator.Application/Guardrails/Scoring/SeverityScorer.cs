using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Guardrails.Scoring;

public sealed class SeverityScorer
{
    private readonly SeverityConfig _config;

    public SeverityScorer(SeverityConfig config)
    {
        _config = config ?? new SeverityConfig();
    }

    public SeverityScore Score(IReadOnlyList<GuardrailViolation> violations)
    {
        if (violations == null || violations.Count == 0)
        {
            return new SeverityScore(0.0, "none");
        }

        double total = 0.0;
        foreach (var violation in violations)
        {
            var key = violation.Severity?.Trim().ToLowerInvariant();
            if (key is not null && _config.Weights.TryGetValue(key, out var weight))
            {
                total += weight;
            }
            else
            {
                total += _config.DefaultWeight;
            }
        }

        var score = total / violations.Count;
        return new SeverityScore(score, Classify(score));
    }

    private string Classify(double score)
    {
        var low = GetThreshold("low", 0.2);
        var medium = GetThreshold("medium", 0.5);
        var high = GetThreshold("high", 0.8);

        if (score >= high)
        {
            return "critical";
        }

        if (score >= medium)
        {
            return "high";
        }

        if (score >= low)
        {
            return "medium";
        }

        return "low";
    }

    private double GetThreshold(string key, double fallback)
        => _config.Thresholds.TryGetValue(key, out var value) ? value : fallback;
}
