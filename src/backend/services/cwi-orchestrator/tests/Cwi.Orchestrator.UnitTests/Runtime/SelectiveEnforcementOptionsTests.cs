using Cwi.Orchestrator.Application.Runtime;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Runtime;

public sealed class SelectiveEnforcementOptionsTests
{
    [Fact]
    public void Default_options_keep_new_layers_observe_only()
    {
        var options = new SelectiveEnforcementOptions();

        options.Resolve(SelectiveEnforcementFeature.Critic).Should().Be(SelectiveEnforcementMode.Observe);
        options.Resolve(SelectiveEnforcementFeature.Truth).Should().Be(SelectiveEnforcementMode.Observe);
        options.Resolve(SelectiveEnforcementFeature.CacheServing).Should().Be(SelectiveEnforcementMode.Observe);
        options.Resolve(SelectiveEnforcementFeature.ExternalOpinion).Should().Be(SelectiveEnforcementMode.Observe);
        options.Resolve(SelectiveEnforcementFeature.FederatedSummaries).Should().Be(SelectiveEnforcementMode.Observe);
        options.IsEnforced(SelectiveEnforcementFeature.Critic).Should().BeFalse();
    }

    [Fact]
    public void Disabled_mode_turns_layer_off_independently()
    {
        var options = new SelectiveEnforcementOptions
        {
            Critic = SelectiveEnforcementMode.Disabled,
            Truth = SelectiveEnforcementMode.Observe
        };

        options.IsEnabled(SelectiveEnforcementFeature.Critic).Should().BeFalse();
        options.IsEnabled(SelectiveEnforcementFeature.Truth).Should().BeTrue();
    }
}
