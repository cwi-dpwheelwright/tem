using Cwi.Orchestrator.Application;
using Cwi.Orchestrator.Application.Packs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Packs;

public sealed class CorePackRuntimeCatalogTests : IDisposable
{
    private readonly string _rootDirectory;

    public CorePackRuntimeCatalogTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "cwi-core-pack-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDirectory);
    }

    [Fact]
    public async Task GetAsync_loads_managed_pack_from_disk()
    {
        WritePack("cwi-communication-core", "latest", "1.0.0", "Friendly but direct.");
        var options = CreateOptions();
        var catalog = new CorePackRuntimeCatalog(options, NullLogger<CorePackRuntimeCatalog>.Instance);

        var result = await catalog.GetAsync("cwi-communication-core", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Document.Version.Should().Be("1.0.0");
        result.Data.Guidance.Content.Should().Contain("Friendly but direct.");
        result.Data.Document.Anchor.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RefreshAllAsync_reloads_when_anchor_changes()
    {
        WritePack("cwi-communication-core", "latest", "1.0.0", "First tone.");
        var options = CreateOptions();
        var catalog = new CorePackRuntimeCatalog(options, NullLogger<CorePackRuntimeCatalog>.Instance);

        var first = await catalog.GetAsync("cwi-communication-core", CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        WritePack("cwi-communication-core", "latest", "1.1.0", "Updated tone.");
        await catalog.RefreshAllAsync("test", CancellationToken.None);

        var second = await catalog.GetAsync("cwi-communication-core", CancellationToken.None);
        second.IsSuccess.Should().BeTrue();
        second.Data!.Document.Version.Should().Be("1.1.0");
        second.Data.Guidance.Content.Should().Contain("Updated tone.");
        second.Data.Document.Anchor.Should().NotBe(first.Data!.Document.Anchor);
    }

    [Fact]
    public async Task RefreshAllAsync_retains_previous_snapshot_when_update_is_invalid()
    {
        WritePack("cwi-communication-core", "latest", "1.0.0", "Stable tone.");
        var options = CreateOptions();
        var catalog = new CorePackRuntimeCatalog(options, NullLogger<CorePackRuntimeCatalog>.Instance);

        var first = await catalog.GetAsync("cwi-communication-core", CancellationToken.None);
        first.IsSuccess.Should().BeTrue();

        WriteInvalidPack("cwi-communication-core", "latest");
        await catalog.RefreshAllAsync("test", CancellationToken.None);

        var second = await catalog.GetAsync("cwi-communication-core", CancellationToken.None);
        second.IsSuccess.Should().BeTrue();
        second.Data!.Document.Version.Should().Be("1.0.0");
        second.Data.Guidance.Content.Should().Contain("Stable tone.");
        second.Data.Document.Anchor.Should().Be(first.Data!.Document.Anchor);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private ApplicationOptions CreateOptions()
    {
        return new ApplicationOptions(
            packsBaseUrl: "http://packs.local",
            packsDirectory: _rootDirectory,
            graphEnabled: false,
            graphDbConnectionString: null,
            graphName: "cwi_graph",
            graphQueryTimeoutSeconds: 5,
            graphTopK: 5,
            vectorShard1Url: "http://v1",
            vectorShard2Url: "http://v2",
            vectorEmbeddingDimension: 8,
            modelBaseUrl: "http://model",
            modelName: "model-default",
            modelTimeoutSeconds: 5,
            policyVersion: "v1",
            ledgerHashSuiteId: "sha256-v1",
            baselinePackNames: new[] { "cwi-identity", "cwi-communication-core" },
            corePackRefreshMode: CorePackRefreshMode.Watch,
            corePackRefreshIntervalSeconds: 1,
            coreCommunicationPackRef: "latest",
            coreIdentityPackRef: "1.0.0");
    }

    private void WritePack(string packName, string packRef, string version, string content)
    {
        var packDirectory = Path.Combine(_rootDirectory, packName, packRef);
        var contentDirectory = Path.Combine(packDirectory, "content");
        Directory.CreateDirectory(contentDirectory);

        var packJson = $$"""
        {
          "pack_id": "{{packName}}",
          "pack_version": "{{version}}",
          "pack_family": "cwi_core",
          "modules": [
            { "id": "module-1", "role": "{{(packName == "cwi-identity" ? "system_truth" : "global_communication_layer")}}" }
          ],
          "activation": {
            "always_load": true,
            "runtime_required": true,
            "load_order": {{(packName == "cwi-identity" ? "1" : "2")}}
          },
          "runtime_hooks": {
            "apply_stage": "final_response_composition",
            "hook_name": "apply_communication_core"
          }
        }
        """;

        var anchor = PackAnchor.ComputeHash(packJson);
        File.WriteAllText(Path.Combine(packDirectory, "pack.json"), packJson);
        File.WriteAllText(Path.Combine(packDirectory, "anchor.txt"), anchor);
        File.WriteAllText(
            Path.Combine(contentDirectory, packName.Replace('-', '_') + ".txt"),
            content);
    }

    private void WriteInvalidPack(string packName, string packRef)
    {
        var packDirectory = Path.Combine(_rootDirectory, packName, packRef);
        Directory.CreateDirectory(packDirectory);
        File.WriteAllText(Path.Combine(packDirectory, "pack.json"), "{ invalid json");
        File.WriteAllText(Path.Combine(packDirectory, "anchor.txt"), "broken-anchor");
    }
}
