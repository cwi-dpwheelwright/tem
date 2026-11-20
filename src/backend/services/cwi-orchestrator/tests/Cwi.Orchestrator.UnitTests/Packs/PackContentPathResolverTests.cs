using System.Text.Json;
using Cwi.Orchestrator.Application.Packs;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Packs;

public sealed class PackContentPathResolverTests : IDisposable
{
    private readonly string _rootDirectory;

    public PackContentPathResolverTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "cwi-pack-content-path-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDirectory);
    }

    [Fact]
    public void ResolveVersionedRelativePath_uses_declared_module_path()
    {
        using var json = JsonDocument.Parse("""
        {
          "pack_id": "cwi-intent-interpreter",
          "modules": [
            {
              "module_id": "intent_rules",
              "type": "text",
              "path": "content/intent_rules.txt"
            }
          ]
        }
        """);

        var path = PackContentPathResolver.ResolveVersionedRelativePath(
            "cwi-intent-interpreter",
            "1.0.0",
            json.RootElement);

        path.Should().Be("1.0.0/content/intent_rules.txt");
    }

    [Fact]
    public void ResolveVersionedRelativePath_falls_back_to_conventional_name_and_latest()
    {
        using var json = JsonDocument.Parse("""
        {
          "pack_id": "cwi-identity"
        }
        """);

        var path = PackContentPathResolver.ResolveVersionedRelativePath(
            "cwi-identity",
            packVersion: null,
            json.RootElement);

        path.Should().Be("latest/content/cwi_identity.txt");
    }

    [Fact]
    public void ResolveLocalPath_uses_declared_module_path_before_conventional_name()
    {
        var packRoot = Path.Combine(_rootDirectory, "cwi-response-orchestrator", "latest");
        var contentDirectory = Path.Combine(packRoot, "content");
        Directory.CreateDirectory(contentDirectory);
        var expectedPath = Path.Combine(contentDirectory, "response_rules.txt");
        File.WriteAllText(expectedPath, "Structured output rules.");

        using var json = JsonDocument.Parse("""
        {
          "pack_id": "cwi-response-orchestrator",
          "modules": [
            {
              "module_id": "response_rules",
              "type": "text",
              "path": "content/response_rules.txt"
            }
          ]
        }
        """);

        var path = PackContentPathResolver.ResolveLocalPath(
            packRoot,
            "cwi-response-orchestrator",
            json.RootElement);

        path.Should().Be(expectedPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }
}
