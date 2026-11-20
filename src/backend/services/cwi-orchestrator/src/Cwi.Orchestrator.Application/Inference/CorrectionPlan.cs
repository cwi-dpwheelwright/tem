#nullable enable
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Inference;

public sealed class CorrectionPlan
{
    public string? CorrectedAnswer { get; init; }
    public IReadOnlyList<CorrectionDiff> Corrections { get; init; } = Array.Empty<CorrectionDiff>();
}
#nullable disable
