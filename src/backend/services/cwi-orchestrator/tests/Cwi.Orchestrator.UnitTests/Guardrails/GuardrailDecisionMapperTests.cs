using Cwi.Orchestrator.Application.Guardrails.Governance;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Guardrails;

public class GuardrailDecisionMapperTests
{
    [Theory]
    [InlineData("none", "allow", false, true)]
    [InlineData("low", "allow", false, true)]
    [InlineData("medium", "ticket", true, false)]
    [InlineData("high", "block", false, false)]
    [InlineData("critical", "block", false, false)]
    public void Map_outputs_expected_outcome(string classification, string outcome, bool requiresApproval, bool allowExecution)
    {
        var mapper = new GuardrailDecisionMapper(new GuardrailDecisionConfig
        {
            AllowThreshold = "low",
            TicketThreshold = "medium"
        });

        var decision = mapper.Map(classification);

        decision.Outcome.Should().Be(outcome);
        decision.RequiresApproval.Should().Be(requiresApproval);
        decision.AllowExecution.Should().Be(allowExecution);
    }
}
