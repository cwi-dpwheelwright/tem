#nullable enable
using System.Text.Json;
using Cwi.Orchestrator.Application.Guardrails.Parsing;
using Cwi.Orchestrator.Application.Guardrails.Units;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Simulation;

public interface ISimulationFeasibilityEvaluator
{
    Task<SimulationFeasibilityResult> EvaluateAsync(
        SimulationFeasibilityRequest request,
        CancellationToken cancellationToken);
}

public sealed class SimulationFeasibilityEvaluator : ISimulationFeasibilityEvaluator
{
    private static readonly string[] HardStopTerms =
    {
        "teleport",
        "time travel",
        "perpetual",
        "infinite energy",
        "warp drive",
        "antigravity"
    };

    public Task<SimulationFeasibilityResult> EvaluateAsync(
        SimulationFeasibilityRequest request,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Payload is null || request.Payload.Count == 0)
        {
            return Task.FromResult(new SimulationFeasibilityResult { IsFeasible = true });
        }

        var textBlobs = ExtractText(request.Payload, request.ActionType);
        if (textBlobs.Count == 0)
        {
            return Task.FromResult(new SimulationFeasibilityResult { IsFeasible = true });
        }

        var violations = new List<GuardrailViolation>();
        foreach (var text in textBlobs)
        {
            if (IsTimedOut(started, request.TimeBudget))
            {
                return Task.FromResult(new SimulationFeasibilityResult
                {
                    IsFeasible = false,
                    Severity = "medium",
                    Reason = "Simulation feasibility timed out.",
                    TimedOut = true
                });
            }

            if (ContainsHardStop(text))
            {
                violations.Add(new GuardrailViolation
                {
                    Id = "simulation.impossible",
                    Name = "Simulation feasibility",
                    Severity = "high",
                    Message = "Payload includes non-physical action cues."
                });
            }

            foreach (var quantity in QuantityExtractor.Extract(text))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsTimedOut(started, request.TimeBudget))
                {
                    return Task.FromResult(new SimulationFeasibilityResult
                    {
                        IsFeasible = false,
                        Severity = "medium",
                        Reason = "Simulation feasibility timed out.",
                        TimedOut = true
                    });
                }

                var normalized = UnitNormalizer.Normalize(quantity);
                if (!normalized.Success || normalized.Normalized is null)
                {
                    continue;
                }

                var normalizedQuantity = normalized.Normalized.Value;
                if (normalizedQuantity.Value < 0 && IsNonNegativeDimension(normalizedQuantity.Dimension))
                {
                    violations.Add(new GuardrailViolation
                    {
                        Id = "simulation.negative_quantity",
                        Name = "Simulation feasibility",
                        Severity = "high",
                        Message = $"Negative {normalizedQuantity.Dimension.ToString().ToLowerInvariant()} detected."
                    });
                }
            }
        }

        if (violations.Count == 0)
        {
            return Task.FromResult(new SimulationFeasibilityResult { IsFeasible = true });
        }

        return Task.FromResult(new SimulationFeasibilityResult
        {
            IsFeasible = false,
            Severity = "high",
            Reason = "Simulation feasibility check failed.",
            Violations = violations
        });
    }

    private static List<string> ExtractText(IReadOnlyDictionary<string, object?> payload, string actionType)
    {
        var results = new List<string>();
        if (!string.IsNullOrWhiteSpace(actionType))
        {
            results.Add(actionType);
        }

        foreach (var value in payload.Values)
        {
            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                results.Add(text);
                continue;
            }

            if (value is JsonElement element && element.ValueKind == JsonValueKind.String)
            {
                var extracted = element.GetString();
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    results.Add(extracted);
                }
            }
        }

        return results;
    }

    private static bool ContainsHardStop(string text)
    {
        foreach (var term in HardStopTerms)
        {
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNonNegativeDimension(Dimension dimension)
    {
        return dimension is Dimension.Mass or Dimension.Length or Dimension.Volume or Dimension.Time;
    }

    private static bool IsTimedOut(DateTimeOffset startedUtc, TimeSpan timeBudget)
    {
        if (timeBudget <= TimeSpan.Zero)
        {
            return false;
        }

        return (DateTimeOffset.UtcNow - startedUtc) > timeBudget;
    }
}
#nullable disable
