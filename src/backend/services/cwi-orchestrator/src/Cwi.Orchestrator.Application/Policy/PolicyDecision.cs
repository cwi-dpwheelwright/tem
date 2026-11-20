#nullable enable
namespace Cwi.Orchestrator.Application.Policy;

public sealed record PolicyDecision(
    PolicyMode Mode,
    RiskLevel Risk,
    bool RequiresApproval,
    bool AllowExecution,
    string Reason);
#nullable disable
