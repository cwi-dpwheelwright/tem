using Cwi.Orchestrator.Application.Packs;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Packs;

public sealed class CorePackOrderingTests
{
    [Fact]
    public void MergeRequiredPackNames_prepends_required_packs_once_in_runtime_order()
    {
        var merged = CorePackOrdering.MergeRequiredPackNames(new[]
        {
            "custom-pack",
            "cwi-communication-core",
            "cwi-identity"
        });

        merged.Should().ContainInOrder(
            "cwi-intent-interpreter",
            "cwi-tone-adaptation",
            "cwi-intent-to-tone-map",
            "cwi-audience-profiles",
            "cwi-memory-policy",
            "cwi-memory-capsule",
            "cwi-source-retrieval",
            "cwi-identity",
            "cwi-values-core",
            "cwi-values-biblical",
            "cwi-ethical-reasoner",
            "cwi-safety-guardrails",
            "cwi-confidence-policy",
            "cwi-response-templates",
            "cwi-communication-core",
            "cwi-first-response-optimizer",
            "cwi-conversation-style-continuity",
            "cwi-response-refinement",
            "cwi-response-orchestrator",
            "custom-pack");
        merged.Should().ContainSingle(pack => pack == "cwi-identity");
        merged.Should().ContainSingle(pack => pack == "cwi-communication-core");
    }

    [Fact]
    public void PresentCorePacks_returns_only_present_core_packs_in_runtime_order()
    {
        var present = CorePackOrdering.PresentCorePacks(new[]
        {
            "cwi-safety-guardrails",
            "custom-pack",
            "cwi-source-retrieval",
            "cwi-identity",
            "cwi-values-core"
        });

        present.Should().Equal(
            "cwi-source-retrieval",
            "cwi-identity",
            "cwi-values-core",
            "cwi-safety-guardrails");
    }
}
