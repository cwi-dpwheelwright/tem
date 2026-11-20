using Cwi.Orchestrator.Application.Packs;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Packs;

public sealed class PackManifestReaderTests
{
    [Fact]
    public void Read_extracts_core_runtime_metadata()
    {
        const string json = """
        {
          "pack_id": "cwi-communication-core",
          "pack_family": "cwi_core",
          "activation": {
            "load_order": 2,
            "always_load": true
          },
          "runtime_required": true,
          "dependencies": {
            "packs": [
              { "pack_id": "cwi-identity" }
            ]
          },
          "retrieval": {
            "force_match_queries": [
              "what is cwi",
              "what does cwi stand for"
            ]
          },
          "confidence_policy": {
            "floor": 0.95
          },
          "scope": {
            "applies_to": [
              "all_domains",
              "all_users"
            ]
          },
          "runtime_hooks": {
            "items": [
              {
                "apply_stage": "final_response_composition",
                "hook_name": "apply_communication_core"
              }
            ]
          },
          "modules": [
            {
              "name": "communication",
              "role": "global_communication_layer"
            }
          ]
        }
        """;

        var document = new PackDocument(
            "cwi-communication-core",
            "1.0.0",
            json,
            "anchor",
            "hash",
            PackContractType.Legacy,
            new PackDescriptor("cwi-communication-core", "cwi-communication-core", "1.0.0", "hash", "anchor", PackContractType.Legacy, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

        var metadata = PackManifestReader.Read(document);

        metadata.PackFamily.Should().Be("cwi_core");
        metadata.ActivationLoadOrder.Should().Be(2);
        metadata.AlwaysLoad.Should().BeTrue();
        metadata.RuntimeRequired.Should().BeTrue();
        metadata.Dependencies.Should().ContainSingle().Which.Should().Be("cwi-identity");
        metadata.ForceMatchQueries.Should().Contain("what is cwi");
        metadata.ConfidenceFloor.Should().Be(0.95f);
        metadata.ScopeAppliesTo.Should().Contain(new[] { "all_domains", "all_users" });
        metadata.ApplyStage.Should().Be("final_response_composition");
        metadata.HookName.Should().Be("apply_communication_core");
        metadata.HasModuleRole("global_communication_layer").Should().BeTrue();
    }
}
