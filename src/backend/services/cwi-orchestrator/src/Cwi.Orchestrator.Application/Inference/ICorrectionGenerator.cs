#nullable enable
namespace Cwi.Orchestrator.Application.Inference;

public interface ICorrectionGenerator
{
    CorrectionPlan Generate(string? answer, DiscrepancyEvaluation evaluation);
}
#nullable disable
