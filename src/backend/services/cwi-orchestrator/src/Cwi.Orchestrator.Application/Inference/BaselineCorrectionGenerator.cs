#nullable enable
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Inference;

public sealed class BaselineCorrectionGenerator : ICorrectionGenerator
{
    public CorrectionPlan Generate(string? answer, DiscrepancyEvaluation evaluation)
    {
        if (evaluation.Corrections.Count == 0)
        {
            return new CorrectionPlan
            {
                Corrections = Array.Empty<CorrectionDiff>()
            };
        }

        var correctedAnswer = ApplyCorrections(answer, evaluation.Corrections);
        return new CorrectionPlan
        {
            CorrectedAnswer = correctedAnswer,
            Corrections = evaluation.Corrections
        };
    }

    private static string? ApplyCorrections(string? answer, IReadOnlyList<CorrectionDiff> corrections)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return null;
        }

        var updated = answer;
        var changed = false;

        foreach (var correction in corrections)
        {
            if (string.IsNullOrWhiteSpace(correction.From) || string.IsNullOrWhiteSpace(correction.To))
            {
                continue;
            }

            var replaced = updated.Replace(
                correction.From,
                correction.To,
                StringComparison.OrdinalIgnoreCase);

            if (!string.Equals(updated, replaced, StringComparison.Ordinal))
            {
                updated = replaced;
                changed = true;
            }
        }

        return changed ? updated : null;
    }
}
#nullable disable
