#nullable enable
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Inference;

public sealed class DiscrepancyEvaluation
{
    public string Severity { get; init; } = "low";

    public string? Reason { get; init; }

    public double Score { get; init; }

    public IReadOnlyList<DiscrepancyRecord> Discrepancies { get; init; } = Array.Empty<DiscrepancyRecord>();

    public IReadOnlyList<CorrectionDiff> Corrections { get; init; } = Array.Empty<CorrectionDiff>();

    public MultiSourceAlignmentDto? Alignment { get; init; }

    public string? FallbackAnswer { get; init; }
}
#nullable disable
