using Cwi.Orchestrator.Application.Guardrails.Governance;
using Cwi.Orchestrator.Application.Guardrails.Scoring;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Guardrails.Provenance;

public sealed class GuardrailLedgerPayload
{
    public string TraceId { get; init; } = string.Empty;
    public string Flow { get; init; } = string.Empty;
    public string? PolicyMode { get; init; }
    public string? ActionType { get; init; }
    public string? ModelId { get; init; }
    public string? PackHash { get; init; }
    public string? PackVersion { get; init; }
    public string? PolicyVersion { get; init; }
    public IReadOnlyList<GuardrailViolation> Violations { get; init; } = Array.Empty<GuardrailViolation>();
    public SeverityScore Score { get; init; }
    public GuardrailDecision? Decision { get; init; }
    public DateTimeOffset EvaluatedUtc { get; init; }
}
