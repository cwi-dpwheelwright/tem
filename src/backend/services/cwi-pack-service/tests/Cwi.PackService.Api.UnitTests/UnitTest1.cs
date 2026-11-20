using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cwi.PackService.Api.UnitTests;

public sealed class PackReadEndpointsTests : IClassFixture<PackReadEndpointsTests.PackServiceFixture>
{
    private readonly PackServiceFixture _fixture;

    public PackReadEndpointsTests(PackServiceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PackJson_ReturnsEtagAndCacheControl()
    {
        var response = await _fixture.Client.GetAsync("/packs/demo-pack/pack.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.ETag!.Tag.Should().Be("\"demo-anchor\"");
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl!.MustRevalidate.Should().BeTrue();
        response.Headers.CacheControl!.MaxAge.Should().Be(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task PackJson_ReturnsNotModifiedWhenEtagMatches()
    {
        var first = await _fixture.Client.GetAsync("/packs/demo-pack/pack.json");
        var etag = first.Headers.ETag?.Tag;
        etag.Should().NotBeNull();

        var request = new HttpRequestMessage(HttpMethod.Get, "/packs/demo-pack/pack.json");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var response = await _fixture.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Theory]
    [InlineData("/packs/broken-pack/pack.json")]
    [InlineData("/packs/staged-pack/pack.json")]
    public async Task PackJson_HidesInvalidOrDependencyBrokenPacks(string path)
    {
        var response = await _fixture.Client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/packs/../pack.json")]
    [InlineData("/packs/%2e%2e/pack.json")]
    public async Task PackJson_RejectsTraversal(string path)
    {
        var response = await _fixture.Client.GetAsync(path);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PackJson_ServesTeacherStyleCorePacks()
    {
        var identity = await _fixture.Client.GetAsync("/packs/cwi-identity/pack.json");
        var communication = await _fixture.Client.GetAsync("/packs/cwi-communication-core/pack.json");

        identity.StatusCode.Should().Be(HttpStatusCode.OK);
        communication.StatusCode.Should().Be(HttpStatusCode.OK);

        var identityPayload = await identity.Content.ReadAsStringAsync();
        var communicationPayload = await communication.Content.ReadAsStringAsync();

        identityPayload.Should().Contain("\"pack_id\": \"cwi-identity\"");
        communicationPayload.Should().Contain("\"pack_id\": \"cwi-communication-core\"");
    }

    [Fact]
    public async Task IndexJson_FiltersOutPacksWithMissingDependencies()
    {
        var response = await _fixture.Client.GetAsync("/packs/index.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("demo-pack");
        payload.Should().Contain("cwi-identity");
        payload.Should().Contain("cwi-communication-core");
        payload.Should().NotContain("broken-pack");
        payload.Should().NotContain("staged-pack");
        payload.Should().NotContain("orphan-pack");
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.Public.Should().BeTrue();
    }

    [Fact]
    public async Task Diagnostics_ReportsFilteredAndUnindexedPacks()
    {
        var response = await _fixture.Client.GetAsync("/packs/diagnostics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = payload.RootElement;

        root.GetProperty("indexed_pack_count").GetInt32().Should().Be(5);
        root.GetProperty("discovered_pack_count").GetInt32().Should().Be(6);
        root.GetProperty("available_pack_count").GetInt32().Should().Be(3);
        root.GetProperty("filtered_pack_count").GetInt32().Should().Be(2);
        root.GetProperty("unindexed_pack_count").GetInt32().Should().Be(1);

        var packs = root.GetProperty("packs")
            .EnumerateArray()
            .ToDictionary(pack => pack.GetProperty("pack_id").GetString()!, pack => pack, StringComparer.OrdinalIgnoreCase);

        packs["demo-pack"].GetProperty("status").GetString().Should().Be("available");
        packs["cwi-identity"].GetProperty("status").GetString().Should().Be("available");
        packs["cwi-communication-core"].GetProperty("status").GetString().Should().Be("available");
        packs["broken-pack"].GetProperty("status").GetString().Should().Be("filtered");
        packs["staged-pack"].GetProperty("status").GetString().Should().Be("filtered");
        packs["orphan-pack"].GetProperty("status").GetString().Should().Be("unindexed");
        packs["orphan-pack"].GetProperty("dependency_graph_available").GetBoolean().Should().BeTrue();

        packs["broken-pack"].GetProperty("issues")
            .EnumerateArray()
            .Select(issue => issue.GetProperty("code").GetString())
            .Should().Contain("dependency_unavailable");

        packs["staged-pack"].GetProperty("issues")
            .EnumerateArray()
            .Select(issue => issue.GetProperty("code").GetString())
            .Should().Contain("schema_invalid");

        packs["orphan-pack"].GetProperty("issues")
            .EnumerateArray()
            .Select(issue => issue.GetProperty("code").GetString())
            .Should().Contain("index_entry_missing");
    }

    [Fact]
    public async Task AvailabilitySummary_ReportsSanitizedValidationReasons()
    {
        var response = await _fixture.Client.GetAsync("/packs/availability");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = payload.RootElement;

        root.GetProperty("status").GetString().Should().Be("available");
        root.GetProperty("available_pack_count").GetInt32().Should().Be(3);
        root.GetProperty("filtered_pack_count").GetInt32().Should().Be(2);
        root.GetProperty("unindexed_pack_count").GetInt32().Should().Be(1);

        var reasons = root.GetProperty("reasons")
            .EnumerateArray()
            .ToDictionary(reason => reason.GetProperty("code").GetString()!, reason => reason, StringComparer.OrdinalIgnoreCase);

        reasons.Keys.Should().Contain(new[] { "missing_dependencies", "invalid_pack_metadata", "unindexed_packs" });
        reasons["missing_dependencies"].GetProperty("count").GetInt32().Should().Be(1);
        reasons["invalid_pack_metadata"].GetProperty("count").GetInt32().Should().Be(1);
        reasons["unindexed_packs"].GetProperty("count").GetInt32().Should().Be(1);
        reasons.Values
            .Select(reason => reason.GetProperty("message").GetString())
            .Should().OnlyContain(message => !string.IsNullOrWhiteSpace(message));
    }

    public sealed class PackServiceFixture : IAsyncLifetime
    {
        private WebApplicationFactory<Program>? _factory;
        private string? _packsRoot;

        public HttpClient Client { get; private set; } = default!;

        public Task InitializeAsync()
        {
            _packsRoot = Path.Combine(Path.GetTempPath(), $"packs-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_packsRoot);

            CreatePack(
                packId: "demo-pack",
                version: "1.0.0",
                packJson: """
                          {
                            "pack_id": "demo-pack",
                            "pack_version": "1.0.0",
                            "name": "Demo Pack",
                            "modules": []
                          }
                          """,
                anchor: "demo-anchor");

            CreatePack(
                packId: "broken-pack",
                version: "1.0.0",
                packJson: """
                          {
                            "pack_id": "broken-pack",
                            "pack_version": "1.0.0",
                            "name": "Broken Pack",
                            "modules": [],
                            "dependencies": {
                              "packs": [
                                { "pack_id": "missing-pack" }
                              ]
                            }
                          }
                          """,
                anchor: "broken-anchor");

            CreatePack(
                packId: "cwi-identity",
                version: "1.0.0",
                packJson: """
                          {
                            "pack_id": "cwi-identity",
                            "pack_version": "1.0.0",
                            "name": "CWI Identity Pack",
                            "description": "Canonical identity truth.",
                            "pack_family": "cwi_core",
                            "scope": {
                              "applies_to": ["all_domains", "all_users", "all_nodes"],
                              "priority": 1000
                            },
                            "activation": {
                              "load_order": 1,
                              "always_load": true,
                              "runtime_required": true
                            },
                            "retrieval": {
                              "force_match_queries": ["what is cwi"]
                            },
                            "confidence_policy": {
                              "system_truth_queries_default_confidence": 0.98
                            },
                            "modules": [
                              {
                                "module_id": "identity_definition",
                                "type": "text",
                                "path": "content/cwi_identity.txt",
                                "role": "system_truth"
                              }
                            ]
                          }
                          """,
                anchor: "identity-anchor");

            CreatePack(
                packId: "cwi-communication-core",
                version: "1.0.0",
                packJson: """
                          {
                            "pack_id": "cwi-communication-core",
                            "pack_version": "1.0.0",
                            "name": "CWI Communication Core Pack",
                            "description": "Global communication layer.",
                            "pack_family": "cwi_core",
                            "dependencies": [
                              {
                                "pack_id": "cwi-identity",
                                "minimum_version": "1.0.0",
                                "required": true
                              }
                            ],
                            "activation": {
                              "load_order": 2,
                              "always_load": true,
                              "runtime_required": true
                            },
                            "runtime_hooks": {
                              "apply_stage": "final_response_composition",
                              "hook_name": "apply_communication_core",
                              "required": true
                            },
                            "modules": [
                              {
                                "module_id": "communication_core",
                                "type": "text",
                                "path": "content/cwi_communication_core.txt",
                                "role": "global_communication_layer"
                              }
                            ]
                          }
                          """,
                anchor: "communication-anchor");

            CreatePack(
                packId: "staged-pack",
                version: "0.1.0",
                packJson: """
                          {
                            "pack_name": "staged-pack",
                            "version": "0.1.0",
                            "status": "staged",
                            "files": [
                              "example.txt"
                            ]
                          }
                          """,
                anchor: "staged-anchor");

            CreatePack(
                packId: "orphan-pack",
                version: "2.0.0",
                packJson: """
                          {
                            "pack_id": "orphan-pack",
                            "pack_version": "2.0.0",
                            "name": "Orphan Pack",
                            "modules": []
                          }
                          """,
                anchor: "orphan-anchor");

            File.WriteAllText(
                Path.Combine(_packsRoot, "index.json"),
                """
                {
                  "packs": [
                    {
                      "pack_id": "demo-pack",
                      "name": "Demo Pack",
                      "latest": "demo-anchor",
                      "versions": ["1.0.0"]
                    },
                    {
                      "pack_id": "broken-pack",
                      "name": "Broken Pack",
                      "latest": "broken-anchor",
                      "versions": ["1.0.0"]
                    },
                    {
                      "pack_id": "staged-pack",
                      "name": "Staged Pack",
                      "latest": "staged-anchor",
                      "versions": ["0.1.0"]
                    },
                    {
                      "pack_id": "cwi-identity",
                      "name": "CWI Identity Pack",
                      "latest": "identity-anchor",
                      "versions": ["1.0.0"]
                    },
                    {
                      "pack_id": "cwi-communication-core",
                      "name": "CWI Communication Core Pack",
                      "latest": "communication-anchor",
                      "versions": ["1.0.0"]
                    }
                  ]
                }
                """);

            Environment.SetEnvironmentVariable("PACKS_DIR", _packsRoot);
            Environment.SetEnvironmentVariable("PACK_SERVICE_PORT", "0");
            Environment.SetEnvironmentVariable("AUTH_REQUIRED", "false");

            _factory = new WebApplicationFactory<Program>();
            Client = _factory.CreateClient();

            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            Client.Dispose();
            _factory?.Dispose();

            if (!string.IsNullOrWhiteSpace(_packsRoot) && Directory.Exists(_packsRoot))
            {
                Directory.Delete(_packsRoot, recursive: true);
            }

            return Task.CompletedTask;
        }

        private void CreatePack(string packId, string version, string packJson, string anchor)
        {
            if (string.IsNullOrWhiteSpace(_packsRoot))
            {
                throw new InvalidOperationException("packs root must be initialized before creating packs.");
            }

            var packRoot = Path.Combine(_packsRoot, packId);
            var versionRoot = Path.Combine(packRoot, version);
            var latestRoot = Path.Combine(packRoot, "latest");
            Directory.CreateDirectory(packRoot);
            Directory.CreateDirectory(versionRoot);
            Directory.CreateDirectory(latestRoot);

            File.WriteAllText(Path.Combine(packRoot, "pack.json"), packJson);
            File.WriteAllText(Path.Combine(packRoot, "anchor.txt"), anchor);
            File.WriteAllText(Path.Combine(versionRoot, "pack.json"), packJson);
            File.WriteAllText(Path.Combine(versionRoot, "anchor.txt"), anchor);
            File.WriteAllText(Path.Combine(latestRoot, "pack.json"), packJson);
            File.WriteAllText(Path.Combine(latestRoot, "anchor.txt"), anchor);
        }
    }
}
