using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Policy;

namespace Cwi.Orchestrator.Application.Packs.Policy;

public sealed class PackCapabilityPolicyEvaluator
{
    public ServiceResponse<PackCapabilityDecision> Evaluate(
        string policyMode,
        IReadOnlyList<Packs.PackCapability> capabilities)
    {
        if (!PolicyModeParser.TryParse(policyMode, out var mode))
        {
            return ServiceResponse<PackCapabilityDecision>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Unknown policy_mode."),
                statusCode: 400,
                message: "Invalid policy_mode");
        }

        if (capabilities.Count == 0)
        {
            return ServiceResponse<PackCapabilityDecision>.Success(new PackCapabilityDecision(
                policyMode,
                RiskLevel.Low,
                requiresApproval: false,
                allowExecution: true,
                reason: "No pack capabilities to evaluate.",
                blockingCapabilities: Array.Empty<string>(),
                evaluatedUtc: DateTimeOffset.UtcNow));
        }

        var highestRisk = RiskLevel.Low;
        var blocking = new List<string>();
        foreach (var capability in capabilities)
        {
            var risk = capability.Risk ?? (capability.RequiresApproval ? RiskLevel.High : RiskLevel.Low);
            highestRisk = risk > highestRisk ? risk : highestRisk;

            var requiresApproval = mode switch
            {
                PolicyMode.Approve => true,
                PolicyMode.AutoWithinBounds when risk == RiskLevel.High => true,
                _ => capability.RequiresApproval
            };

            if (requiresApproval)
            {
                blocking.Add(capability.Name);
            }
        }

        var decisionRequiresApproval = blocking.Count > 0;
        var allowExecution = !decisionRequiresApproval;
        var reason = decisionRequiresApproval
            ? "Pack capability requires approval."
            : "Pack capabilities permitted.";

        return ServiceResponse<PackCapabilityDecision>.Success(new PackCapabilityDecision(
            policyMode,
            highestRisk,
            decisionRequiresApproval,
            allowExecution,
            reason,
            blocking,
            DateTimeOffset.UtcNow));
    }
}
