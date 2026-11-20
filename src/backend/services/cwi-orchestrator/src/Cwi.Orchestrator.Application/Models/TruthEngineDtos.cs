#nullable enable
namespace Cwi.Orchestrator.Application.Models;

public sealed record TruthEngineDecisionDto(
    string Version,
    string Mode,
    string State,
    string Reason,
    int EvidenceCount,
    int ConflictCount,
    IReadOnlyList<string> EvidenceKinds,
    DateTimeOffset EvaluatedUtc);
#nullable disable
