#nullable enable
using Cwi.Orchestrator.Application.Policy;

namespace Cwi.Orchestrator.Application.Inference;

public sealed class DiscrepancyFallbackSelector : IFallbackSelector
{
    public FallbackSelection Select(
        string? answer,
        DiscrepancyEvaluation evaluation,
        CorrectionPlan correctionPlan,
        string policyMode,
        string? survivalMode,
        string? survivalOfflineMessage)
    {
        if (string.Equals(survivalMode, "survival", StringComparison.OrdinalIgnoreCase))
        {
            var message = string.IsNullOrWhiteSpace(survivalOfflineMessage)
                ? "Service is in survival mode. Please retry later."
                : survivalOfflineMessage;
            return new FallbackSelection
            {
                Applied = true,
                Answer = message,
                Reason = "Survival mode active.",
                Mode = "survival"
            };
        }

        var approveMode = PolicyModeParser.TryParse(policyMode, out var parsed)
            && parsed == PolicyMode.Approve;

        if (approveMode && !string.IsNullOrWhiteSpace(correctionPlan.CorrectedAnswer))
        {
            return new FallbackSelection
            {
                Applied = true,
                Answer = correctionPlan.CorrectedAnswer,
                Reason = "Applied baseline correction.",
                Mode = "correction"
            };
        }

        if (approveMode && !string.IsNullOrWhiteSpace(evaluation.FallbackAnswer))
        {
            return new FallbackSelection
            {
                Applied = true,
                Answer = evaluation.FallbackAnswer,
                Reason = "Fallback response issued due to high-severity discrepancy.",
                Mode = "safe"
            };
        }

        return new FallbackSelection
        {
            Applied = false,
            Answer = answer,
            Reason = null,
            Mode = null
        };
    }
}
#nullable disable
