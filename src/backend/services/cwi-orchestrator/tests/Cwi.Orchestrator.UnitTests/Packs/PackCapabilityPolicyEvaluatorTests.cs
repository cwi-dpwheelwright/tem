using Cwi.Orchestrator.Application.Packs;
using Cwi.Orchestrator.Application.Packs.Policy;
using Cwi.Orchestrator.Application.Policy;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Packs;

public sealed class PackCapabilityPolicyEvaluatorTests
{
    [Fact]
    public void Evaluate_ApproveMode_RequiresApproval()
    {
        var evaluator = new PackCapabilityPolicyEvaluator();
        var capabilities = new[]
        {
            new PackCapability("search", "ask", RiskLevel.Low, requiresApproval: false, description: null)
        };

        var result = evaluator.Evaluate("approve", capabilities);

        result.IsSuccess.Should().BeTrue();
        result.Data!.RequiresApproval.Should().BeTrue();
        result.Data.AllowExecution.Should().BeFalse();
        result.Data.BlockingCapabilities.Should().Contain("search");
    }

    [Fact]
    public void Evaluate_AutoWithinBounds_HighRisk_Blocks()
    {
        var evaluator = new PackCapabilityPolicyEvaluator();
        var capabilities = new[]
        {
            new PackCapability("action:shutdown", "action", RiskLevel.High, requiresApproval: false, description: null)
        };

        var result = evaluator.Evaluate("auto_within_bounds", capabilities);

        result.IsSuccess.Should().BeTrue();
        result.Data!.RequiresApproval.Should().BeTrue();
        result.Data.AllowExecution.Should().BeFalse();
        result.Data.Risk.Should().Be(RiskLevel.High);
    }

    [Fact]
    public void Evaluate_ObserveMode_ExplicitApproval_Blocks()
    {
        var evaluator = new PackCapabilityPolicyEvaluator();
        var capabilities = new[]
        {
            new PackCapability("export", "action", RiskLevel.Medium, requiresApproval: true, description: null)
        };

        var result = evaluator.Evaluate("observe", capabilities);

        result.IsSuccess.Should().BeTrue();
        result.Data!.RequiresApproval.Should().BeTrue();
        result.Data.AllowExecution.Should().BeFalse();
        result.Data.BlockingCapabilities.Should().Contain("export");
    }

    [Fact]
    public void Evaluate_InvalidPolicyMode_Fails()
    {
        var evaluator = new PackCapabilityPolicyEvaluator();
        var capabilities = new[]
        {
            new PackCapability("search", "ask", RiskLevel.Low, requiresApproval: false, description: null)
        };

        var result = evaluator.Evaluate("nope", capabilities);

        result.IsSuccess.Should().BeFalse();
    }
}
