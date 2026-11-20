#nullable enable
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.OperatorMemory;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.OperatorMemory;

public class OperatorProfilePolicyServiceTests
{
    [Fact]
    public void Evaluate_applies_conservative_policy_on_low_correlation()
    {
        var options = new OperatorMemoryOptions
        {
            CorrelationScoreThreshold = 0.7
        };
        var service = new OperatorProfilePolicyService(options);
        var profile = new OperatorProfileSummaryDto
        {
            TenantId = "tenant",
            OperatorId = "operator",
            SignalsTotal = 10,
            CorrectionRate = 0.2,
            DelayRate = 0.1,
            ConfirmationRate = 0.7,
            CorrelationScore = 0.5,
            LastUpdatedUtc = System.DateTimeOffset.UtcNow
        };

        var decision = service.Evaluate("standard", profile);

        decision.Applied.Should().BeTrue();
        decision.PolicyMode.Should().Be("conservative");
        decision.RuleId.Should().Be("profile:low_correlation");
    }
}
#nullable disable
