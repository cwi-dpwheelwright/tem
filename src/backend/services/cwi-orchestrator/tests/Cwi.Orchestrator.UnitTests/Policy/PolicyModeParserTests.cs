using Cwi.Orchestrator.Application.Policy;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Policy;

public sealed class PolicyModeParserTests
{
    [Fact]
    public void TryParse_standard_alias_maps_to_observe()
    {
        var parsed = PolicyModeParser.TryParse("standard", out var mode);

        parsed.Should().BeTrue();
        mode.Should().Be(PolicyMode.Observe);
    }
}
