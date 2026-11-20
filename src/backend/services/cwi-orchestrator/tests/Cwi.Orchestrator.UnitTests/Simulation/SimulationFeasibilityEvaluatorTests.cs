#nullable enable
using System.Collections.Generic;
using System.Threading;
using Cwi.Orchestrator.Application.Simulation;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Simulation;

public class SimulationFeasibilityEvaluatorTests
{
    [Fact]
    public async Task Evaluate_flags_non_physical_terms()
    {
        ISimulationFeasibilityEvaluator evaluator = new SimulationFeasibilityEvaluator();
        var payload = new Dictionary<string, object?>
        {
            ["instruction"] = "Teleport the payload instantly."
        };

        var request = new SimulationFeasibilityRequest("teleport_action", payload, TimeSpan.Zero);
        var result = await evaluator.EvaluateAsync(request, CancellationToken.None);

        result.IsFeasible.Should().BeFalse();
        result.Violations.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Evaluate_flags_negative_mass()
    {
        ISimulationFeasibilityEvaluator evaluator = new SimulationFeasibilityEvaluator();
        var payload = new Dictionary<string, object?>
        {
            ["mass"] = "-5 kg"
        };

        var request = new SimulationFeasibilityRequest("set_mass", payload, TimeSpan.Zero);
        var result = await evaluator.EvaluateAsync(request, CancellationToken.None);

        result.IsFeasible.Should().BeFalse();
        result.Violations.Should().NotBeEmpty();
    }
}
#nullable disable
