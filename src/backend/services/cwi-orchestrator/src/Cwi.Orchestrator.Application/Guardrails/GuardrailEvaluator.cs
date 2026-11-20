using Cwi.Orchestrator.Application.Guardrails.Config;
using Cwi.Orchestrator.Application.Guardrails.Diagnostics;
using Cwi.Orchestrator.Application.Guardrails.Scoring;
using Cwi.Orchestrator.Application.Guardrails.Validators;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Guardrails;

public sealed class GuardrailEvaluator
{
    private readonly GuardrailValidatorRegistry _registry;
    private readonly SeverityScorer _scorer;

    public GuardrailEvaluator(GuardrailValidatorRegistry registry, SeverityScorer scorer)
    {
        _registry = registry;
        _scorer = scorer;
    }

    public GuardrailEvaluationResult Evaluate(GuardrailContext context, GuardrailConfig config)
    {
        var violations = new List<GuardrailViolation>();
        foreach (var validatorConfig in config.Validators)
        {
            if (!_registry.TryGet(validatorConfig.Id, out var validator))
            {
                violations.Add(new GuardrailViolation
                {
                    Id = "guardrail_registry",
                    Name = "Guardrail registry",
                    Severity = "high",
                    Message = $"Unknown guardrail validator '{validatorConfig.Id}'."
                });
                continue;
            }

            var settings = new GuardrailValidatorSettings(validatorConfig.Settings);
            var result = validator.Validate(context, settings);
            if (result.Violations.Count > 0)
            {
                violations.AddRange(result.Violations);
            }
        }

        var score = _scorer.Score(violations);
        return new GuardrailEvaluationResult
        {
            Violations = violations,
            Score = score,
            Diagnostics = BuildDiagnostics(violations)
        };
    }

    private static GuardrailDiagnostics BuildDiagnostics(IReadOnlyList<GuardrailViolation> violations)
    {
        var severityCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var validatorCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var violation in violations)
        {
            if (!string.IsNullOrWhiteSpace(violation.Severity))
            {
                var key = violation.Severity.Trim().ToLowerInvariant();
                severityCounts[key] = severityCounts.TryGetValue(key, out var count) ? count + 1 : 1;
            }

            if (!string.IsNullOrWhiteSpace(violation.Id))
            {
                var key = violation.Id.Trim().ToLowerInvariant();
                validatorCounts[key] = validatorCounts.TryGetValue(key, out var count) ? count + 1 : 1;
            }
        }

        return new GuardrailDiagnostics
        {
            TotalViolations = violations.Count,
            SeverityCounts = severityCounts,
            ValidatorCounts = validatorCounts
        };
    }
}
