using Cwi.Orchestrator.Application.Policy;

namespace Cwi.Orchestrator.Application.Packs;

public sealed class PackCapability
{
    public string Name { get; }
    public string? Scope { get; }
    public RiskLevel? Risk { get; }
    public bool RequiresApproval { get; }
    public string? Description { get; }

    public PackCapability(
        string name,
        string? scope,
        RiskLevel? risk,
        bool requiresApproval,
        string? description)
    {
        Name = name;
        Scope = scope;
        Risk = risk;
        RequiresApproval = requiresApproval;
        Description = description;
    }
}
