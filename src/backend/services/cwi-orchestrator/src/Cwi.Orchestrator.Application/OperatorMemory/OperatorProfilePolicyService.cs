#nullable enable
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.OperatorMemory;

public sealed class OperatorProfilePolicyService
{
    private readonly OperatorMemoryOptions _options;

    public OperatorProfilePolicyService(OperatorMemoryOptions options)
    {
        _options = options;
    }

    public OperatorProfileDecisionDto Evaluate(string currentPolicyMode, OperatorProfileSummaryDto profile)
    {
        var normalizedPolicy = string.IsNullOrWhiteSpace(currentPolicyMode)
            ? "standard"
            : currentPolicyMode.Trim().ToLowerInvariant();

        if (profile.CorrelationScore < _options.CorrelationScoreThreshold)
        {
            return BuildDecision(normalizedPolicy, "conservative", true, "profile:low_correlation");
        }

        if (profile.CorrectionRate > _options.CorrectionRateThreshold)
        {
            return BuildDecision(normalizedPolicy, "conservative", true, "profile:high_corrections");
        }

        return BuildDecision(normalizedPolicy, normalizedPolicy, false, "profile:within_bounds");
    }

    private static OperatorProfileDecisionDto BuildDecision(
        string currentPolicyMode,
        string nextPolicyMode,
        bool applied,
        string ruleId)
    {
        if (!applied || string.Equals(currentPolicyMode, nextPolicyMode, StringComparison.OrdinalIgnoreCase))
        {
            return new OperatorProfileDecisionDto
            {
                Applied = false,
                PolicyMode = currentPolicyMode,
                Reason = "Profile within policy bounds.",
                RuleId = ruleId
            };
        }

        return new OperatorProfileDecisionDto
        {
            Applied = true,
            PolicyMode = nextPolicyMode,
            Reason = $"Policy mode adjusted to {nextPolicyMode} based on operator profile.",
            RuleId = ruleId
        };
    }
}
#nullable disable
