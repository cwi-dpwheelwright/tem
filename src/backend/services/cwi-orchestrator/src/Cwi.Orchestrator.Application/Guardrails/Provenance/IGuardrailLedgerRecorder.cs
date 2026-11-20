using Cwi.Orchestrator.Application.Guardrails.Governance;
using Cwi.Orchestrator.Application.Guardrails.Scoring;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Guardrails.Provenance;

public interface IGuardrailLedgerRecorder
{
    Task RecordAsync(
        string traceId,
        string flow,
        string? policyMode,
        string? actionType,
        string? modelId,
        string? packHash,
        string? packVersion,
        string? policyVersion,
        IReadOnlyList<GuardrailViolation> violations,
        SeverityScore score,
        GuardrailDecision? decision,
        CancellationToken cancellationToken);
}
