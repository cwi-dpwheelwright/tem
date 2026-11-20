using Cwi.Orchestrator.Application.Policy;

namespace Cwi.Orchestrator.Application.Packs.Policy;

public sealed class PackCapabilityDecision
{
    public string PolicyMode { get; }
    public RiskLevel Risk { get; }
    public bool RequiresApproval { get; }
    public bool AllowExecution { get; }
    public string Reason { get; }
    public IReadOnlyList<string> BlockingCapabilities { get; }
    public DateTimeOffset EvaluatedUtc { get; }

    public PackCapabilityDecision(
        string policyMode,
        RiskLevel risk,
        bool requiresApproval,
        bool allowExecution,
        string reason,
        IReadOnlyList<string> blockingCapabilities,
        DateTimeOffset evaluatedUtc)
    {
        PolicyMode = policyMode;
        Risk = risk;
        RequiresApproval = requiresApproval;
        AllowExecution = allowExecution;
        Reason = reason;
        BlockingCapabilities = blockingCapabilities;
        EvaluatedUtc = evaluatedUtc;
    }
}
