using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.Health;
using Cwi.Router.Application.Routing;

namespace Cwi.Router.Application.Clients;

public sealed class MeshAssessmentClient
{
    private readonly HttpClient _client;
    private readonly RouterConfig _config;

    public MeshAssessmentClient(IHttpClientFactory httpClientFactory, RouterConfig config)
    {
        _client = httpClientFactory.CreateClient("coordination");
        _config = config;
    }

    public async Task<MeshHealthSnapshot?> GetHealthAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.CoordinationLayerUrl))
        {
            return null;
        }

        try
        {
            var uri = new Uri(new Uri(_config.CoordinationLayerUrl), "/api/mesh/health");
            var response = await _client.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<MeshHealthPayload>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (payload is null)
            {
                return null;
            }

            return new MeshHealthSnapshot
            {
                Status = string.IsNullOrWhiteSpace(payload.Status) ? "unknown" : payload.Status,
                TotalNodes = payload.TotalNodes,
                DegradedNodes = payload.DegradedNodes,
                DownNodes = payload.DownNodes,
                TotalLinks = payload.TotalLinks,
                DegradedLinks = payload.DegradedLinks,
                DownLinks = payload.DownLinks,
                GeneratedUtc = payload.GeneratedUtc ?? DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<MeshRoutingHint?> GetHintAsync(
        string nodeId,
        string taskComplexity,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.CoordinationLayerUrl))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return null;
        }

        try
        {
            var uri = new Uri(new Uri(_config.CoordinationLayerUrl), "/api/mesh/hints");
            var payload = new MeshHintRequest
            {
                NodeId = nodeId,
                TaskComplexity = taskComplexity
            };

            using var response = await _client.PostAsJsonAsync(uri, payload, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var hint = await response.Content.ReadFromJsonAsync<MeshHintResponse>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (hint is null)
            {
                return null;
            }

            return new MeshRoutingHint
            {
                PreferredInterface = hint.PreferredInterface,
                PreferredPath = hint.PreferredPath,
                Confidence = hint.Confidence,
                Reason = hint.Reason,
                FallbackInterfaces = hint.FallbackInterfaces ?? Array.Empty<string>()
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed class MeshHealthPayload
    {
        [JsonPropertyName("status")]
        public string? Status { get; init; }
        [JsonPropertyName("total_nodes")]
        public int TotalNodes { get; init; }
        [JsonPropertyName("degraded_nodes")]
        public int DegradedNodes { get; init; }
        [JsonPropertyName("down_nodes")]
        public int DownNodes { get; init; }
        [JsonPropertyName("total_links")]
        public int TotalLinks { get; init; }
        [JsonPropertyName("degraded_links")]
        public int DegradedLinks { get; init; }
        [JsonPropertyName("down_links")]
        public int DownLinks { get; init; }
        [JsonPropertyName("generated_utc")]
        public DateTimeOffset? GeneratedUtc { get; init; }
    }

    private sealed class MeshHintRequest
    {
        [JsonPropertyName("node_id")]
        public string NodeId { get; init; } = string.Empty;
        [JsonPropertyName("task_complexity")]
        public string TaskComplexity { get; init; } = "medium";
    }

    private sealed class MeshHintResponse
    {
        [JsonPropertyName("preferred_interface")]
        public string PreferredInterface { get; init; } = string.Empty;
        [JsonPropertyName("preferred_path")]
        public string? PreferredPath { get; init; }
        [JsonPropertyName("confidence")]
        public double Confidence { get; init; }
        [JsonPropertyName("reason")]
        public string Reason { get; init; } = string.Empty;
        [JsonPropertyName("fallback_interfaces")]
        public IReadOnlyList<string>? FallbackInterfaces { get; init; }
    }
}
