using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Cwi.Router.Application.Routing;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cwi.Router.IntegrationTests;

public sealed class NetworkAssessmentIntegrationTests
{
    [Fact]
    public async Task Ask_Includes_Mesh_Reroute_And_Attestation_Metadata()
    {
        RouterReadinessTestHelpers.ConfigureEnvironment(redisUrl: string.Empty);
        Environment.SetEnvironmentVariable("COORDINATION_LAYER_URL", "http://coordination");
        Environment.SetEnvironmentVariable("ROUTER_SURVIVAL_ENABLED", "false");
        Environment.SetEnvironmentVariable("ROUTER_NETWORK_ASSESSMENT_ENABLED", "true");
        Environment.SetEnvironmentVariable("ROUTER_MESH_HEALTH_ENABLED", "true");
        Environment.SetEnvironmentVariable("ROUTER_MESH_HINT_ENABLED", "true");
        Environment.SetEnvironmentVariable("ROUTER_NETWORK_REROUTE_ON_DEGRADED", "true");

        var orchestratorHandler = new RecordingHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        var coordinationHandler = new CoordinationHandler();
        var modelProbeHandler = new OkHandler();

        using var factory = CreateFactory(orchestratorHandler, coordinationHandler, modelProbeHandler);
        using var client = factory.CreateClient();

        var question = string.Join(" ", Enumerable.Repeat("word", 300));
        var payload = new
        {
            tenant_id = "tenant-1",
            user_id = "user-9",
            question,
            pack = new { name = "default" },
            context = new { mode = "observe", top_k = 8 },
            network_assessment = new
            {
                node_id = "node-17",
                observed_utc = "2026-02-02T12:00:00Z",
                interfaces = new[]
                {
                    new
                    {
                        @interface = "lte",
                        state = "degraded",
                        signal_quality = 0.25,
                        latency_ms = 420,
                        jitter_ms = 18,
                        packet_loss = 0.2,
                        throughput_mbps = 6.5
                    }
                }
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/ask", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("X-Routing-Network-Status", out var networkStatus).Should().BeTrue();
        networkStatus!.Should().ContainSingle().Which.Should().Be("down");
        response.Headers.TryGetValues("X-Routing-Mesh-Health", out var meshHealth).Should().BeTrue();
        meshHealth!.Should().ContainSingle().Which.Should().Be("degraded");
        response.Headers.TryGetValues("X-Routing-Reroute-Required", out var rerouteRequired).Should().BeTrue();
        rerouteRequired!.Should().ContainSingle().Which.Should().Be("true");
        response.Headers.TryGetValues("X-Route-Attestation-Id", out var attestationId).Should().BeTrue();
        attestationId!.Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();

        var hintRequest = coordinationHandler.LastHintRequest;
        hintRequest.Should().NotBeNull();
        hintRequest!.NodeId.Should().Be("node-17");
        hintRequest.TaskComplexity.Should().Be("high");

        orchestratorHandler.Requests.Should().NotBeEmpty();
        var forwarded = orchestratorHandler.Requests.Last();
        forwarded.Headers.TryGetValue("X-Route-Attestation", out var attestationHeader).Should().BeTrue();
        var attestation = JsonSerializer.Deserialize<RouteAttestationEnvelope>(attestationHeader!);
        attestation.Should().NotBeNull();
        attestation!.NodeId.Should().Be("node-17");
        attestation.RerouteRequired.Should().BeTrue();
        attestation.RerouteReason.Should().Contain("network_down");
    }

    private static WebApplicationFactory<Program> CreateFactory(
        HttpMessageHandler orchestratorHandler,
        HttpMessageHandler coordinationHandler,
        HttpMessageHandler modelProbeHandler)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddHttpClient("orchestrator")
                        .ConfigurePrimaryHttpMessageHandler(() => orchestratorHandler);
                    services.AddHttpClient("coordination")
                        .ConfigurePrimaryHttpMessageHandler(() => coordinationHandler);
                    services.AddHttpClient("model-probe")
                        .ConfigurePrimaryHttpMessageHandler(() => modelProbeHandler);
                });
            });
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _responder;

        public RecordingHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            var headers = request.Headers.ToDictionary(
                header => header.Key,
                header => string.Join(",", header.Value));

            if (request.Content is not null)
            {
                foreach (var header in request.Content.Headers)
                {
                    headers[header.Key] = string.Join(",", header.Value);
                }
            }

            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body, headers));
            return _responder(request, body);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string? Body,
        Dictionary<string, string> Headers);

    private sealed class CoordinationHandler : HttpMessageHandler
    {
        public RoutingHintRequestSnapshot? LastHintRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/api/mesh/health", StringComparison.OrdinalIgnoreCase))
            {
                var payload = "{" +
                              "\"status\":\"degraded\"," +
                              "\"total_nodes\":5," +
                              "\"degraded_nodes\":2," +
                              "\"down_nodes\":1," +
                              "\"total_links\":8," +
                              "\"degraded_links\":2," +
                              "\"down_links\":1," +
                              "\"nodes\":[]," +
                              "\"generated_utc\":\"2026-02-02T12:00:00Z\"" +
                              "}";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
            }

            if (path.EndsWith("/api/mesh/hints", StringComparison.OrdinalIgnoreCase))
            {
                var body = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);

                if (!string.IsNullOrWhiteSpace(body))
                {
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    LastHintRequest = new RoutingHintRequestSnapshot(
                        root.GetProperty("node_id").GetString() ?? string.Empty,
                        root.GetProperty("task_complexity").GetString() ?? string.Empty);
                }

                var response = "{" +
                               "\"preferred_interface\":\"lte\"," +
                               "\"preferred_path\":null," +
                               "\"confidence\":0.88," +
                               "\"reason\":\"mesh_degraded\"," +
                               "\"fallback_interfaces\":[\"sat\"]" +
                               "}";

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(response, Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed record RoutingHintRequestSnapshot(string NodeId, string TaskComplexity);

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty)
            };
            return Task.FromResult(response);
        }
    }
}
