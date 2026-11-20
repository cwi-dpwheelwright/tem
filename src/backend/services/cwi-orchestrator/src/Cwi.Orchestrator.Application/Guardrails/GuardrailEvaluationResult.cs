using Cwi.Orchestrator.Application.Guardrails.Diagnostics;
using Cwi.Orchestrator.Application.Guardrails.Scoring;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Guardrails;

public sealed class GuardrailEvaluationResult
{
    public IReadOnlyList<GuardrailViolation> Violations { get; init; } = Array.Empty<GuardrailViolation>();
    public SeverityScore Score { get; init; }
    public GuardrailDiagnostics Diagnostics { get; init; } = new();
}
