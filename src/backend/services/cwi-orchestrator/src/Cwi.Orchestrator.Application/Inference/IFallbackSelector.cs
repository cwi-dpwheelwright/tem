#nullable enable
namespace Cwi.Orchestrator.Application.Inference;

public interface IFallbackSelector
{
    FallbackSelection Select(
        string? answer,
        DiscrepancyEvaluation evaluation,
        CorrectionPlan correctionPlan,
        string policyMode,
        string? survivalMode,
        string? survivalOfflineMessage);
}
#nullable disable
