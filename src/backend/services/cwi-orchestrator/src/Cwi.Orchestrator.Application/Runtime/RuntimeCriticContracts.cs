#nullable enable
using System.Collections.Generic;

namespace Cwi.Orchestrator.Application.Runtime;

public sealed record RuntimeCriticResult(
    string Version,
    string Mode,
    double OverallScore,
    IReadOnlyList<RuntimeCriticDimensionScore> Scores,
    IReadOnlyList<RuntimeCriticFinding> Findings);

public sealed record RuntimeCriticDimensionScore(
    string Dimension,
    double Score,
    string Reason);

public sealed record RuntimeCriticFinding(
    string Severity,
    string Category,
    string Rule,
    string Detail);
#nullable disable
