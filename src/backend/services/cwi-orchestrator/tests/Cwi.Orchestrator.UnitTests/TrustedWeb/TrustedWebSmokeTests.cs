using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Orchestrator.Application.Model;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Packs;
using Cwi.Orchestrator.Application.Trace;
using Cwi.Orchestrator.Application.TrustedWeb;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Cwi.Orchestrator.UnitTests.TrustedWeb;

public sealed class TrustedWebSmokeTests
{
    private static readonly JsonSerializerOptions OutputJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string SmokeAskPayload = """
        {
          "tenant_id": "tenant-1",
          "user_id": "user-1",
          "question": "What changed in the current market update?",
          "pack": {
            "name": "enterprise"
          },
          "context": {
            "mode": "observe",
            "top_k": 3,
            "conversation_id": "conv-smoke",
            "web_retrieval": {
              "enabled": true,
              "mode": "include",
              "profile": "general",
              "max_results": 1
            }
          }
        }
        """;

    private readonly ITestOutputHelper _output;

    public TrustedWebSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Ask_payload_with_trusted_web_include_returns_result_trace_and_citation()
    {
        var tenantConfigRepository = new InMemoryTenantConfigRepository();
        var trustedWebConfigService = new TrustedWebConfigService(tenantConfigRepository);
        await trustedWebConfigService.SaveAsync(
            "tenant-1",
            new TrustedWebConfigDto
            {
                Enabled = true,
                DefaultProfile = "general",
                Profiles = new Dictionary<string, TrustedWebProfileConfigDto>
                {
                    ["general"] = new()
                    {
                        Mode = "trusted_sites_only",
                        MaxResults = 1,
                        Sources = new[]
                        {
                            new TrustedWebSourceConfigDto
                            {
                                SourceId = "trusted-market",
                                DisplayName = "Trusted Market",
                                AllowedDomains = new[] { "trusted.example" },
                                SeedUrls = new[] { "https://trusted.example/news/current-market-update" }
                            }
                        }
                    }
                }
            },
            "admin-user",
            CancellationToken.None);
        var http = new StubHttpClientFactory(new Dictionary<string, StubResponse>
        {
            ["https://trusted.example/news/current-market-update"] = Html("""
                <html>
                  <head><title>Current Market Update</title></head>
                  <body>
                    <h1>Current Market Update</h1>
                    <p>Trusted market update: backlog changed and vessel availability improved this week.</p>
                  </body>
                </html>
                """)
        });
        var trustedWebOptions = new TrustedWebContextOptions
        {
            Enabled = true,
            DefaultProfile = "general",
            MaxResults = 3,
            RespectRobots = false
        };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var trustedWebProvider = new ConfiguredTrustedWebContextProvider(http, trustedWebOptions, cache, trustedWebConfigService);
        var modelClient = new RecordingModelClient();
        var journeyContext = JourneyTestHelpers.CreateContext("request-web-smoke");
        var pipeline = new AskWorkflowPipeline(
            new IAskWorkflowStep[]
            {
                new AskTrustedWebContextStep(trustedWebOptions, trustedWebProvider, JourneyTestHelpers.CreateEmitter("request-web-smoke")),
                new AskPromptStep(new PromptBuilder(), CreateBaselinePackContentProvider(journeyContext)),
                new AskInferenceStep(modelClient, JourneyTestHelpers.CreateEmitter("request-web-smoke")),
                new AskResponseStep(new TraceLookupStore(), journeyContext)
            },
            Array.Empty<IAskWorkflowDecorator>());
        var service = new AskService(pipeline, CreateApplicationOptions(), journeyContext);
        var request = JsonSerializer.Deserialize<AskRequestDto>(SmokeAskPayload);

        request.Should().NotBeNull();
        var response = await service.HandleAsync(
            request!.TenantId,
            request.UserId,
            request.Question,
            request.Pack.Name,
            request.Pack.Anchor,
            request.Context.Mode,
            request.Context.TopK,
            modelTier: null,
            request.Context.ConversationId,
            request.Context.Timezone,
            CancellationToken.None,
            request.Context.WebRetrieval);

        _output.WriteLine("ASK_INPUT_PAYLOAD:");
        _output.WriteLine(SmokeAskPayload);
        _output.WriteLine("ASK_RESPONSE:");
        _output.WriteLine(JsonSerializer.Serialize(response.Data, OutputJsonOptions));

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Answer.Should().Contain("Smoke answer from native model path.");
        response.Data.Answer.Should().Contain("Trusted web sources consulted: [1] Current Market Update.");
        response.Data.Answer.Should().NotContain("Web sources:");
        response.Data.AnswerMarkdown.Should().Be(response.Data.Answer);
        response.Data.Trace.TrustedWebRequested.Should().BeTrue();
        response.Data.Trace.TrustedWebEnabled.Should().BeTrue();
        response.Data.Trace.TrustedWebProfile.Should().Be("general");
        response.Data.Trace.TrustedWebHits.Should().Be(1);
        response.Data.Trace.RetrievalStatus.Should().Be("success");
        response.Data.Trace.Sources.Should().Contain("web:trusted-market:news-current-market-update");
        var trustedWebTraceSource = response.Data.Trace.TrustedWebSources.Should().ContainSingle(source =>
            source.SourceId == "trusted-market"
            && source.EvidenceSourceId == "web:trusted-market:news-current-market-update"
            && source.Marker == 1
            && source.Url == "https://trusted.example/news/current-market-update"
            && source.Title == "Current Market Update"
            && source.ChunkId == "news-current-market-update").Subject;
        trustedWebTraceSource.ContentSha256.Should().HaveLength(64);
        response.Data.Citations.Should().ContainSingle(citation =>
            citation.DocId == "web:trusted-market"
            && citation.ChunkId == "news-current-market-update"
            && citation.Marker == 1
            && citation.SourceId == "web:trusted-market:news-current-market-update"
            && citation.Title == "Current Market Update"
            && citation.Url == "https://trusted.example/news/current-market-update"
            && citation.ContentSha256!.Length == 64
            && citation.Score > 0);
        modelClient.LastPrompt.Should().Contain("Trusted web evidence, supplemental only:");
        modelClient.LastPrompt.Should().Contain("[1] source_id: web:trusted-market:news-current-market-update");
        modelClient.LastPrompt.Should().Contain("Content SHA-256:");
        modelClient.LastPrompt.Should().Contain("Trusted market update: backlog changed");
        modelClient.LastPrompt.Should().Contain("https://trusted.example/news/current-market-update");
        http.RequestedUris.Should().Contain("https://trusted.example/news/current-market-update");
    }

    [Fact]
    public async Task Ask_payload_with_trusted_web_include_reports_degraded_no_evidence_without_citations()
    {
        var tenantConfigRepository = new InMemoryTenantConfigRepository();
        var trustedWebConfigService = new TrustedWebConfigService(tenantConfigRepository);
        await trustedWebConfigService.SaveAsync(
            "tenant-1",
            new TrustedWebConfigDto
            {
                Enabled = true,
                DefaultProfile = "general",
                Profiles = new Dictionary<string, TrustedWebProfileConfigDto>
                {
                    ["general"] = new()
                    {
                        Mode = "trusted_sites_only",
                        MaxResults = 1,
                        Sources = new[]
                        {
                            new TrustedWebSourceConfigDto
                            {
                                SourceId = "trusted-market",
                                DisplayName = "Trusted Market",
                                AllowedDomains = new[] { "trusted.example" },
                                SeedUrls = new[] { "https://trusted.example/news/no-match" }
                            }
                        }
                    }
                }
            },
            "admin-user",
            CancellationToken.None);
        var http = new StubHttpClientFactory(new Dictionary<string, StubResponse>
        {
            ["https://trusted.example/news/no-match"] = Html("""
                <html>
                  <head><title>Quarterly Archive</title></head>
                  <body>
                    <p>This page talks about archived facility maintenance only.</p>
                  </body>
                </html>
                """)
        });
        var trustedWebOptions = new TrustedWebContextOptions
        {
            Enabled = true,
            DefaultProfile = "general",
            MaxResults = 3,
            RespectRobots = false
        };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var trustedWebProvider = new ConfiguredTrustedWebContextProvider(http, trustedWebOptions, cache, trustedWebConfigService);
        var modelClient = new RecordingModelClient("Native answer without current web evidence.");
        var journeyContext = JourneyTestHelpers.CreateContext("request-web-no-evidence");
        var pipeline = new AskWorkflowPipeline(
            new IAskWorkflowStep[]
            {
                new AskTrustedWebContextStep(trustedWebOptions, trustedWebProvider, JourneyTestHelpers.CreateEmitter("request-web-no-evidence")),
                new AskPromptStep(new PromptBuilder(), CreateBaselinePackContentProvider(journeyContext)),
                new AskInferenceStep(modelClient, JourneyTestHelpers.CreateEmitter("request-web-no-evidence")),
                new AskResponseStep(new TraceLookupStore(), journeyContext)
            },
            Array.Empty<IAskWorkflowDecorator>());
        var service = new AskService(pipeline, CreateApplicationOptions(), journeyContext);
        var request = JsonSerializer.Deserialize<AskRequestDto>(SmokeAskPayload);

        request.Should().NotBeNull();
        var response = await service.HandleAsync(
            request!.TenantId,
            request.UserId,
            request.Question,
            request.Pack.Name,
            request.Pack.Anchor,
            request.Context.Mode,
            request.Context.TopK,
            modelTier: null,
            request.Context.ConversationId,
            request.Context.Timezone,
            CancellationToken.None,
            request.Context.WebRetrieval);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Answer.Should().Contain("Trusted web evidence was unavailable");
        response.Data.Citations.Should().BeEmpty();
        response.Data.UnsupportedClaims.Should().Contain("trusted_web_evidence_unavailable");
        response.Data.Trace.TrustedWebRequested.Should().BeTrue();
        response.Data.Trace.TrustedWebEnabled.Should().BeTrue();
        response.Data.Trace.TrustedWebHits.Should().Be(0);
        response.Data.Trace.RetrievalStatus.Should().Be("none");
        response.Data.Trace.TrustedWebSources.Should().BeEmpty();
    }

    [Fact]
    public async Task Ask_payload_with_trusted_web_include_removes_fabricated_numeric_citation_markers()
    {
        var tenantConfigRepository = new InMemoryTenantConfigRepository();
        var trustedWebConfigService = new TrustedWebConfigService(tenantConfigRepository);
        await trustedWebConfigService.SaveAsync(
            "tenant-1",
            new TrustedWebConfigDto
            {
                Enabled = true,
                DefaultProfile = "general",
                Profiles = new Dictionary<string, TrustedWebProfileConfigDto>
                {
                    ["general"] = new()
                    {
                        Mode = "trusted_sites_only",
                        MaxResults = 1,
                        Sources = new[]
                        {
                            new TrustedWebSourceConfigDto
                            {
                                SourceId = "trusted-market",
                                DisplayName = "Trusted Market",
                                AllowedDomains = new[] { "trusted.example" },
                                SeedUrls = new[] { "https://trusted.example/news/current-market-update" }
                            }
                        }
                    }
                }
            },
            "admin-user",
            CancellationToken.None);
        var http = new StubHttpClientFactory(new Dictionary<string, StubResponse>
        {
            ["https://trusted.example/news/current-market-update"] = Html("""
                <html>
                  <head><title>Current Market Update</title></head>
                  <body>
                    <p>Trusted market update: backlog changed and vessel availability improved this week.</p>
                  </body>
                </html>
                """)
        });
        var trustedWebOptions = new TrustedWebContextOptions
        {
            Enabled = true,
            DefaultProfile = "general",
            MaxResults = 3,
            RespectRobots = false
        };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var trustedWebProvider = new ConfiguredTrustedWebContextProvider(http, trustedWebOptions, cache, trustedWebConfigService);
        var modelClient = new RecordingModelClient("The market changed materially [9].");
        var journeyContext = JourneyTestHelpers.CreateContext("request-web-fabricated-marker");
        var pipeline = new AskWorkflowPipeline(
            new IAskWorkflowStep[]
            {
                new AskTrustedWebContextStep(trustedWebOptions, trustedWebProvider, JourneyTestHelpers.CreateEmitter("request-web-fabricated-marker")),
                new AskPromptStep(new PromptBuilder(), CreateBaselinePackContentProvider(journeyContext)),
                new AskInferenceStep(modelClient, JourneyTestHelpers.CreateEmitter("request-web-fabricated-marker")),
                new AskResponseStep(new TraceLookupStore(), journeyContext)
            },
            Array.Empty<IAskWorkflowDecorator>());
        var service = new AskService(pipeline, CreateApplicationOptions(), journeyContext);
        var request = JsonSerializer.Deserialize<AskRequestDto>(SmokeAskPayload);

        request.Should().NotBeNull();
        var response = await service.HandleAsync(
            request!.TenantId,
            request.UserId,
            request.Question,
            request.Pack.Name,
            request.Pack.Anchor,
            request.Context.Mode,
            request.Context.TopK,
            modelTier: null,
            request.Context.ConversationId,
            request.Context.Timezone,
            CancellationToken.None,
            request.Context.WebRetrieval);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Answer.Should().NotContain("[9]");
        response.Data.Answer.Should().Contain("[1]");
        response.Data.Warnings.Should().Contain("unsupported_trusted_web_citation_marker_removed:9");
        response.Data.UnsupportedClaims.Should().Contain("unsupported_trusted_web_citation_marker_removed:9");
        response.Data.Citations.Should().ContainSingle(citation => citation.Marker == 1);
    }

    private static BaselinePackContentProvider CreateBaselinePackContentProvider(JourneyRequestContext journeyContext)
    {
        return new BaselinePackContentProvider(
            CreateApplicationOptions(),
            new StaticHttpClientFactory(new HttpClient()),
            new EmptyCorePackRuntimeCatalog(),
            journeyContext,
            NullLogger<BaselinePackContentProvider>.Instance);
    }

    private static ApplicationOptions CreateApplicationOptions()
    {
        return new ApplicationOptions(
            packsBaseUrl: "http://127.0.0.1:1",
            packsDirectory: null,
            graphEnabled: false,
            graphDbConnectionString: null,
            graphName: "cwi_graph",
            graphQueryTimeoutSeconds: 5,
            graphTopK: 5,
            vectorShard1Url: "http://127.0.0.1:1",
            vectorShard2Url: "http://127.0.0.1:1",
            vectorEmbeddingDimension: 64,
            modelBaseUrl: "http://127.0.0.1:1",
            modelName: "smoke-model",
            modelTimeoutSeconds: 1,
            policyVersion: "test-policy",
            ledgerHashSuiteId: "sha256");
    }

    private static StubResponse Html(string body) => new(body, "text/html");

    private sealed class RecordingModelClient : IModelClient
    {
        private readonly string _answer;

        public RecordingModelClient(string answer = "Smoke answer from native model path.")
        {
            _answer = answer;
        }

        public string? LastPrompt { get; private set; }

        public Task<ServiceResponse<ModelResult>> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            LastPrompt = request.Prompt;
            return Task.FromResult(ServiceResponse<ModelResult>.Success(
                new ModelResult("smoke-model", _answer)));
        }
    }

    private sealed class EmptyCorePackRuntimeCatalog : ICorePackRuntimeCatalog
    {
        public bool Enabled => false;

        public IReadOnlyList<string> ManagedPackNames => Array.Empty<string>();

        public bool IsManaged(string packName) => false;

        public bool TryGetCurrent(string packName, out CorePackRuntimeEntry? entry)
        {
            entry = null;
            return false;
        }

        public Task<ServiceResponse<CorePackRuntimeEntry>> GetAsync(string packName, CancellationToken cancellationToken)
        {
            return Task.FromResult(ServiceResponse<CorePackRuntimeEntry>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Core pack not found."),
                statusCode: 404,
                message: "Core pack not found."));
        }

        public Task RefreshAllAsync(string reason, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InMemoryTenantConfigRepository : ITenantConfigRepository
    {
        private readonly Dictionary<string, TenantConfig> _records = new(StringComparer.OrdinalIgnoreCase);

        public Task<TenantConfig?> GetAsync(string tenantId, CancellationToken cancellationToken)
        {
            _records.TryGetValue(tenantId, out var config);
            return Task.FromResult(config);
        }

        public Task UpsertAsync(TenantConfig config, CancellationToken cancellationToken)
        {
            if (_records.TryGetValue(config.TenantId, out var existing))
            {
                existing.ConfigJson = config.ConfigJson;
                existing.UpdatedUtc = config.UpdatedUtc;
            }
            else
            {
                _records[config.TenantId] = config;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StaticHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly StubHttpMessageHandler _handler;

        public StubHttpClientFactory(IReadOnlyDictionary<string, StubResponse> responses)
        {
            _handler = new StubHttpMessageHandler(responses);
        }

        public IReadOnlyList<string> RequestedUris => _handler.RequestedUris;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, StubResponse> _responses;
        private readonly List<string> _requestedUris = new();

        public StubHttpMessageHandler(IReadOnlyDictionary<string, StubResponse> responses)
        {
            _responses = responses;
        }

        public IReadOnlyList<string> RequestedUris => _requestedUris;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            _requestedUris.Add(uri);

            if (!_responses.TryGetValue(uri, out var response))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(string.Empty)
                });
            }

            return Task.FromResult(response.ToHttpResponseMessage());
        }
    }

    private sealed record StubResponse(string Body, string ContentType)
    {
        public HttpResponseMessage ToHttpResponseMessage()
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Body, System.Text.Encoding.UTF8, ContentType)
            };
        }
    }
}
