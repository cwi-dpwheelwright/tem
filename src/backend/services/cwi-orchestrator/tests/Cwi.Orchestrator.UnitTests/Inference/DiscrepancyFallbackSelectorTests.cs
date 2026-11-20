#nullable enable
using Cwi.Orchestrator.Application.Inference;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Inference;

public class DiscrepancyFallbackSelectorTests
{
    [Fact]
    public void Select_prefers_survival_offline_message()
    {
        var selector = new DiscrepancyFallbackSelector();
        var evaluation = new DiscrepancyEvaluation
        {
            Severity = "high",
            FallbackAnswer = "fallback"
        };
        var plan = new CorrectionPlan
        {
            CorrectedAnswer = "corrected"
        };

        var selection = selector.Select(
            "answer",
            evaluation,
            plan,
            policyMode: "approve",
            survivalMode: "survival",
            survivalOfflineMessage: "offline");

        selection.Applied.Should().BeTrue();
        selection.Mode.Should().Be("survival");
        selection.Answer.Should().Be("offline");
    }

    [Fact]
    public void Select_uses_correction_in_approve_mode()
    {
        var selector = new DiscrepancyFallbackSelector();
        var evaluation = new DiscrepancyEvaluation
        {
            Severity = "medium",
            FallbackAnswer = "fallback"
        };
        var plan = new CorrectionPlan
        {
            CorrectedAnswer = "corrected"
        };

        var selection = selector.Select(
            "answer",
            evaluation,
            plan,
            policyMode: "approve",
            survivalMode: "normal",
            survivalOfflineMessage: null);

        selection.Applied.Should().BeTrue();
        selection.Mode.Should().Be("correction");
        selection.Answer.Should().Be("corrected");
    }
}
#nullable disable
