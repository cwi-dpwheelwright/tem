using System.Linq;
using Cwi.Orchestrator.Application.Runtime;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Runtime;

public sealed class RuntimeCapabilityStatusServiceTests
{
    [Fact]
    public void GetMatrix_returns_required_capabilities_without_unproven_healthy_statuses()
    {
        var service = new RuntimeCapabilityStatusService();

        var matrix = service.GetMatrix();

        matrix.Select(item => item.Id).Should().BeEquivalentTo(new[]
        {
            "teacher",
            "critic",
            "process_miner",
            "graph",
            "vector",
            "truth_engine",
            "cache",
            "federated_learning",
            "external_opinion"
        });
        matrix.Should().NotContain(item => item.Status == "healthy");
        matrix.Should().OnlyContain(item => item.Evidence.Count > 0);
        matrix.Single(item => item.Id == "critic").Status.Should().Be("implemented");
        matrix.Single(item => item.Id == "process_miner").Status.Should().Be("implemented");
        matrix.Single(item => item.Id == "truth_engine").Status.Should().Be("implemented");
    }
}
