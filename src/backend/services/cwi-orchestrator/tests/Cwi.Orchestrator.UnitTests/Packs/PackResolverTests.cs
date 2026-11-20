using System.Net;
using System.Text;
using Cwi.Common;
using Cwi.Orchestrator.Application;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Packs;
using Cwi.Orchestrator.Application.Trace;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Packs;

public sealed class PackResolverTests
{
    private const string PacksBaseUrl = "http://packs.local";
    private const string AdoptedManifestPath = "/packs/adopted/current_adoption.json";

    [Fact]
    public async Task ResolveAsync_auto_mode_falls_back_to_legacy_when_adopted_manifest_is_unavailable()
    {
        var handler = new StubHttpMessageHandler();
        var (fixturePackJson, fixtureAnchor) = BuildLegacyPack("fixture-pack", "1.2.3");
        handler.AddJson($"{PacksBaseUrl}/packs/fixture-pack/pack.json", fixturePackJson);
        handler.AddText($"{PacksBaseUrl}/packs/fixture-pack/anchor.txt", fixtureAnchor);

        var resolver = CreateResolver(handler, PackResolverMode.Auto);
        var result = await resolver.ResolveAsync("fixture-pack", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Root.ContractType.Should().Be(PackContractType.Legacy);
        result.Data.Root.Name.Should().Be("fixture-pack");
        result.Data.Root.Hash.Should().Be(fixtureAnchor);
    }

    [Fact]
    public async Task ResolveAsync_legacy_only_does_not_require_adopted_manifest()
    {
        var handler = new StubHttpMessageHandler();
        var (fixturePackJson, fixtureAnchor) = BuildLegacyPack("fixture-pack", "1.2.3");
        handler.AddJson($"{PacksBaseUrl}/packs/fixture-pack/pack.json", fixturePackJson);
        handler.AddText($"{PacksBaseUrl}/packs/fixture-pack/anchor.txt", fixtureAnchor);

        var resolver = CreateResolver(handler, PackResolverMode.LegacyOnly);
        var result = await resolver.ResolveAsync("fixture-pack", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Root.ContractType.Should().Be(PackContractType.Legacy);
    }

    [Fact]
    public async Task ResolveAsync_includes_configured_baseline_pack()
    {
        var handler = new StubHttpMessageHandler();
        var (fixturePackJson, fixtureAnchor) = BuildLegacyPack("fixture-pack", "1.2.3");
        handler.AddJson($"{PacksBaseUrl}/packs/fixture-pack/pack.json", fixturePackJson);
        handler.AddText($"{PacksBaseUrl}/packs/fixture-pack/anchor.txt", fixtureAnchor);

        var (baselinePackJson, baselineAnchor) = BuildLegacyPack("cwi-communication-core", "1.0.0");
        handler.AddJson($"{PacksBaseUrl}/packs/cwi-communication-core/pack.json", baselinePackJson);
        handler.AddText($"{PacksBaseUrl}/packs/cwi-communication-core/anchor.txt", baselineAnchor);

        var resolver = CreateResolver(handler, PackResolverMode.LegacyOnly, new[] { "cwi-communication-core" });
        var result = await resolver.ResolveAsync("fixture-pack", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.BaselinePacks.Should().ContainSingle(pack => pack.Name == "cwi-communication-core");
        result.Data.Dependencies.Should().Contain(pack => pack.Name == "cwi-communication-core");
    }

    [Fact]
    public async Task ResolveAsync_uses_synthetic_default_root_when_general_pack_is_unavailable()
    {
        var handler = new StubHttpMessageHandler();

        var resolver = CreateResolver(handler, PackResolverMode.Auto);
        var result = await resolver.ResolveAsync("general-pack", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Root.Name.Should().Be("general-pack");
        result.Data.Root.Version.Should().Be("runtime-default");
        result.Data.Root.Hash.Should().NotBeNullOrWhiteSpace();
        result.Data.Root.Json.Should().Contain("\"pack_id\":\"general-pack\"");
    }

    [Fact]
    public async Task ResolveAsync_does_not_synthesize_custom_missing_pack()
    {
        var handler = new StubHttpMessageHandler();

        var resolver = CreateResolver(handler, PackResolverMode.Auto);
        var result = await resolver.ResolveAsync("custom-pack", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ResolveAsync_orders_core_baseline_packs_using_preferred_runtime_order()
    {
        var handler = new StubHttpMessageHandler();
        var (fixturePackJson, fixtureAnchor) = BuildLegacyPack("fixture-pack", "1.2.3");
        handler.AddJson($"{PacksBaseUrl}/packs/fixture-pack/pack.json", fixturePackJson);
        handler.AddText($"{PacksBaseUrl}/packs/fixture-pack/anchor.txt", fixtureAnchor);

        AddCoreBaselinePack(handler, "cwi-response-orchestrator", "1.0.0", 3);
        AddCoreBaselinePack(handler, "cwi-safety-guardrails", "1.0.0", 1);
        AddCoreBaselinePack(handler, "cwi-ethical-reasoner", "1.0.0", 1);
        AddCoreBaselinePack(handler, "cwi-values-biblical", "1.0.0", 1);
        AddCoreBaselinePack(handler, "cwi-values-core", "1.0.0", 1);
        AddCoreBaselinePack(handler, "cwi-communication-core", "1.1.0", 2);
        AddCoreBaselinePack(handler, "cwi-identity", "1.0.0", 1);
        AddCoreBaselinePack(handler, "cwi-source-retrieval", "1.0.0", 1);
        AddCoreBaselinePack(handler, "cwi-memory-capsule", "1.0.0", 1);
        AddCoreBaselinePack(handler, "cwi-memory-policy", "1.0.0", 1);
        AddCoreBaselinePack(handler, "cwi-intent-interpreter", "1.0.0", 0);

        var resolver = CreateResolver(
            handler,
            PackResolverMode.LegacyOnly,
            new[]
            {
                "cwi-intent-interpreter",
                "cwi-memory-policy",
                "cwi-memory-capsule",
                "cwi-source-retrieval",
                "cwi-identity",
                "cwi-values-core",
                "cwi-values-biblical",
                "cwi-ethical-reasoner",
                "cwi-safety-guardrails",
                "cwi-communication-core",
                "cwi-response-orchestrator"
            });

        var result = await resolver.ResolveAsync("fixture-pack", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.BaselinePacks.Select(pack => pack.Name).Should().ContainInOrder(
            "cwi-intent-interpreter",
            "cwi-memory-policy",
            "cwi-memory-capsule",
            "cwi-source-retrieval",
            "cwi-identity",
            "cwi-values-core",
            "cwi-values-biblical",
            "cwi-ethical-reasoner",
            "cwi-safety-guardrails",
            "cwi-communication-core",
            "cwi-response-orchestrator");
    }

    [Fact]
    public async Task ResolveAsync_rejects_raw_pack_json_anchor()
    {
        var handler = new StubHttpMessageHandler();
        var (fixturePackJson, _) = BuildLegacyPack("fixture-pack", "1.2.3");
        var rawAnchor = PackAnchor.ComputeRawHash(fixturePackJson);
        handler.AddJson($"{PacksBaseUrl}/packs/fixture-pack/pack.json", fixturePackJson);
        handler.AddText($"{PacksBaseUrl}/packs/fixture-pack/anchor.txt", rawAnchor);

        var resolver = CreateResolver(handler, PackResolverMode.LegacyOnly);
        var result = await resolver.ResolveAsync("fixture-pack", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("Pack verification failed");
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ResolveAsync_resolves_adopted_pack_with_publication_version()
    {
        var handler = new StubHttpMessageHandler();
        var (fixturePackJson, fixtureAnchor) = BuildLegacyPack("fixture-pack", "1.2.3");
        handler.AddJson($"{PacksBaseUrl}/packs/fixture-pack/pack.json", fixturePackJson);
        handler.AddText($"{PacksBaseUrl}/packs/fixture-pack/anchor.txt", fixtureAnchor);

        var (adoptedPackJson, adoptedAnchor) = BuildAdoptedPack("operations-pack", "2026.03.11", null, null);
        var manifestJson = BuildAdoptedManifest("operations-pack", "2026.03.11", "release-20260311T152847Z/operations-pack/operations-pack.json", adoptedAnchor);
        handler.AddJson($"{PacksBaseUrl}{AdoptedManifestPath}", manifestJson);
        handler.AddJson($"{PacksBaseUrl}/packs/adopted/release-20260311T152847Z/operations-pack/operations-pack.json", adoptedPackJson);

        var resolver = CreateResolver(handler, PackResolverMode.Auto);
        var result = await resolver.ResolveAsync("Operations-Pack", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Root.ContractType.Should().Be(PackContractType.Adopted);
        result.Data.Root.Version.Should().Be("2026.03.11");
        result.Data.Root.Hash.Should().Be(adoptedAnchor);
        result.Data.Root.Descriptor.PublicationVersion.Should().Be("2026.03.11");
        result.Data.Root.Descriptor.Entries.Should().Contain("op-entry-1");
    }

    [Fact]
    public async Task ResolveAsync_auto_mode_falls_back_to_legacy_dependency_resolution()
    {
        var handler = new StubHttpMessageHandler();
        var (fixturePackJson, fixtureAnchor) = BuildLegacyPack("fixture-pack", "1.2.3");
        handler.AddJson($"{PacksBaseUrl}/packs/fixture-pack/pack.json", fixturePackJson);
        handler.AddText($"{PacksBaseUrl}/packs/fixture-pack/anchor.txt", fixtureAnchor);

        var (adoptedPackJson, adoptedAnchor) = BuildAdoptedPack("operations-pack", "2026.03.11", "fixture-pack", "1.2.3");
        var manifestJson = BuildAdoptedManifest("operations-pack", "2026.03.11", "release-20260311T152847Z/operations-pack/operations-pack.json", adoptedAnchor);
        handler.AddJson($"{PacksBaseUrl}{AdoptedManifestPath}", manifestJson);
        handler.AddJson($"{PacksBaseUrl}/packs/adopted/release-20260311T152847Z/operations-pack/operations-pack.json", adoptedPackJson);

        var resolver = CreateResolver(handler, PackResolverMode.Auto);
        var result = await resolver.ResolveAsync("operations-pack", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Root.ContractType.Should().Be(PackContractType.Adopted);
        result.Data.Root.Name.Should().Be("operations-pack");
        result.Data.Dependencies.Should().ContainSingle(pack =>
            pack.Name == "fixture-pack" && pack.ContractType == PackContractType.Legacy);
    }

    [Fact]
    public async Task ResolveAsync_fails_on_adopted_anchor_mismatch()
    {
        var handler = new StubHttpMessageHandler();
        var (adoptedPackJson, _) = BuildAdoptedPack("operations-pack", "2026.03.11", null, null);
        var manifestJson = BuildAdoptedManifest("operations-pack", "2026.03.11", "release-20260311T152847Z/operations-pack/operations-pack.json", "deadbeef");
        handler.AddJson($"{PacksBaseUrl}{AdoptedManifestPath}", manifestJson);
        handler.AddJson($"{PacksBaseUrl}/packs/adopted/release-20260311T152847Z/operations-pack/operations-pack.json", adoptedPackJson);

        var resolver = CreateResolver(handler, PackResolverMode.AdoptedOnly);
        var result = await resolver.ResolveAsync("operations-pack", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("Adopted pack verification failed");
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task ResolveAsync_reports_missing_adopted_version()
    {
        var handler = new StubHttpMessageHandler();
        var packJson = """
        {
          "pack_target": "engineering-pack",
          "entries": [ { "id": "eng-1" } ]
        }
        """;
        var anchor = PackAnchor.ComputeHash(packJson);
        var manifestJson = """
        {
          "adoptions": [
            {
              "pack_target": "engineering-pack",
              "anchor_hash": "%ANCHOR%",
              "publication": {
                "slug": "engineering-pack",
                "published_file": "release-20260311T152847Z/engineering-pack/engineering-pack.json"
              }
            }
          ]
        }
        """.Replace("%ANCHOR%", anchor);

        handler.AddJson($"{PacksBaseUrl}{AdoptedManifestPath}", manifestJson);
        handler.AddJson($"{PacksBaseUrl}/packs/adopted/release-20260311T152847Z/engineering-pack/engineering-pack.json", packJson);

        var resolver = CreateResolver(handler, PackResolverMode.AdoptedOnly);
        var result = await resolver.ResolveAsync("engineering-pack", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("Adopted pack version missing");
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task AskPackResolveStep_populates_trace_fields_from_adopted_pack()
    {
        var handler = new StubHttpMessageHandler();
        var (adoptedPackJson, adoptedAnchor) = BuildAdoptedPack("operations-pack", "2026.03.11", null, null);
        var manifestJson = BuildAdoptedManifest("operations-pack", "2026.03.11", "release-20260311T152847Z/operations-pack/operations-pack.json", adoptedAnchor);
        handler.AddJson($"{PacksBaseUrl}{AdoptedManifestPath}", manifestJson);
        handler.AddJson($"{PacksBaseUrl}/packs/adopted/release-20260311T152847Z/operations-pack/operations-pack.json", adoptedPackJson);

        var resolver = CreateResolver(handler, PackResolverMode.AdoptedOnly);
        var journeyContext = JourneyTestHelpers.CreateContext();
        var resolveStep = new AskPackResolveStep(
            resolver,
            NullLogger<AskPackResolveStep>.Instance,
            JourneyTestHelpers.CreateEmitter());
        var responseStep = new AskResponseStep(new TraceLookupStore(), journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "question", "operations-pack", null, "observe", 3);
        var resolveResponse = await resolveStep.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        resolveResponse.IsSuccess.Should().BeTrue();
        context.PackHash.Should().Be(adoptedAnchor);
        context.PackVersion.Should().Be("2026.03.11");

        var response = await responseStep.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.Data!.Trace.RequestId.Should().Be(journeyContext.RequestId);
        response.Data!.Trace.PackHash.Should().Be(adoptedAnchor);
        response.Data.Trace.PackVersion.Should().Be("2026.03.11");
    }

    private static PackResolver CreateResolver(
        StubHttpMessageHandler handler,
        PackResolverMode mode,
        IReadOnlyList<string>? baselinePackNames = null)
    {
        var httpClient = new HttpClient(handler);
        var factory = new StubHttpClientFactory(httpClient);
        var options = new ApplicationOptions(
            packsBaseUrl: PacksBaseUrl,
            packsDirectory: null,
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
            packResolverMode: mode,
            packsAdoptedBaseUrl: PacksBaseUrl,
            packsAdoptedManifestPath: AdoptedManifestPath,
            baselinePackNames: baselinePackNames);

        var journeyContext = JourneyTestHelpers.CreateContext();
        var fetcher = new PackFetcher(options, factory, journeyContext, NullLogger<PackFetcher>.Instance);
        var signatureValidator = new NoopPackSignatureValidator();
        var adopted = new AdoptedPackResolver(
            options,
            factory,
            NullLogger<AdoptedPackResolver>.Instance,
            signatureValidator,
            journeyContext);
        var legacy = new LegacyPackResolver(fetcher, signatureValidator);
        var runtimeCatalog = new CorePackRuntimeCatalog(options, NullLogger<CorePackRuntimeCatalog>.Instance);
        return new PackResolver(
            new IPackContractResolver[] { adopted, legacy },
            options,
            runtimeCatalog,
            NullLogger<PackResolver>.Instance);
    }

    private static (string Json, string Anchor) BuildLegacyPack(string packId, string version)
    {
        var json = $$"""
        {
          "pack_id": "{{packId}}",
          "pack_version": "{{version}}",
          "name": "{{packId}}",
          "modules": [
            { "id": "module-a" },
            { "id": "module-b" }
          ],
          "capabilities": [
            { "name": "shutdown", "risk": "high", "requires_approval": true }
          ]
        }
        """;
        var anchor = PackAnchor.ComputeHash(json);
        return (json, anchor);
    }

    private static void AddCoreBaselinePack(StubHttpMessageHandler handler, string packId, string version, int loadOrder)
    {
        var json = $$"""
        {
          "pack_id": "{{packId}}",
          "pack_version": "{{version}}",
          "name": "{{packId}}",
          "activation": {
            "load_order": {{loadOrder}},
            "always_load": true,
            "runtime_required": true
          },
          "modules": [
            { "id": "module-a" }
          ]
        }
        """;
        var anchor = PackAnchor.ComputeHash(json);
        handler.AddJson($"{PacksBaseUrl}/packs/{packId}/pack.json", json);
        handler.AddText($"{PacksBaseUrl}/packs/{packId}/anchor.txt", anchor);
    }

    private static (string Json, string Anchor) BuildAdoptedPack(string packTarget, string version, string? dependencyPack, string? dependencyVersion)
    {
        var dependencyJson = string.IsNullOrWhiteSpace(dependencyPack)
            ? string.Empty
            : $$"""
            ,
            "dependencies": {
              "packs": [
                { "pack_id": "{{dependencyPack}}", "pack_version": "{{dependencyVersion}}" }
              ]
            }
            """;

        var json = $$"""
        {
          "pack_target": "{{packTarget}}",
          "publication": {
            "slug": "{{packTarget}}",
            "version": "{{version}}",
            "published_file": "release-20260311T152847Z/{{packTarget}}/{{packTarget}}.json",
            "latest_file": "release-20260311T152847Z/{{packTarget}}/{{packTarget}}.json"
          },
          "entries": [
            { "id": "op-entry-1", "name": "entry-1" }
          ],
          "capabilities": [
            { "name": "shutdown", "risk": "high", "requires_approval": true }
          ]{{dependencyJson}}
        }
        """;

        var anchor = PackAnchor.ComputeHash(json);
        return (json, anchor);
    }

    private static string BuildAdoptedManifest(string packTarget, string version, string file, string anchor)
    {
        return $$"""
        {
          "adoptions": [
            {
              "pack_target": "{{packTarget}}",
              "anchor_hash": "{{anchor}}",
              "publication": {
                "slug": "{{packTarget}}",
                "version": "{{version}}",
                "published_file": "{{file}}",
                "latest_file": "{{file}}"
              }
            }
          ]
        }
        """;
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, StubResponse> _responses = new(StringComparer.OrdinalIgnoreCase);

        public void AddJson(string url, string content, HttpStatusCode status = HttpStatusCode.OK)
        {
            _responses[url] = new StubResponse(status, content, "application/json");
        }

        public void AddText(string url, string content, HttpStatusCode status = HttpStatusCode.OK)
        {
            _responses[url] = new StubResponse(status, content, "text/plain");
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            }

            if (_responses.TryGetValue(request.RequestUri.ToString(), out var response))
            {
                return Task.FromResult(response.Build());
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed record StubResponse(HttpStatusCode Status, string Content, string ContentType)
    {
        public HttpResponseMessage Build()
        {
            var message = new HttpResponseMessage(Status)
            {
                Content = new StringContent(Content, Encoding.UTF8, ContentType)
            };
            return message;
        }
    }
}
