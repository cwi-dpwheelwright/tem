#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Workflows;

namespace Cwi.Orchestrator.Application.Runtime;

public sealed class ExternalRuntimeCriticClient : IRuntimeCriticClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly RuntimeCriticOptions _options;

    public ExternalRuntimeCriticClient(HttpClient httpClient, RuntimeCriticOptions options)
    {
        _httpClient = httpClient;
        _options = options;

        if (_options.IsConfigured && _httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(_options.BaseUrl!.TrimEnd('/') + "/");
        }
    }

    public async Task<RuntimeCriticResult?> EvaluateAsync(
        AskWorkflowContext context,
        AskResponseDto response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(response);

        if (!_options.IsConfigured)
        {
            return null;
        }

        using var content = new StringContent(
            JsonSerializer.Serialize(BuildPayload(context, response), JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var httpResponse = await _httpClient
            .PostAsync(_options.EndpointPath, content, cancellationToken)
            .ConfigureAwait(false);

        var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!httpResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"External runtime critic returned {(int)httpResponse.StatusCode}: {Truncate(body, 512)}");
        }

        var payload = JsonSerializer.Deserialize<RuntimeCriticHttpResponse>(body, JsonOptions)
                      ?? throw new InvalidOperationException("External runtime critic returned an empty response.");

        return payload.ToResult();
    }

    public static object BuildPayload(AskWorkflowContext context, AskResponseDto response)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(response);

        return new Dictionary<string, object?>
        {
            ["schema_version"] = "runtime-critic-observation.v1",
            ["tenant_id"] = context.TenantId,
            ["user_id_hash"] = HashUserId(context.UserId),
            ["request_id"] = context.RequestId,
            ["trace_id"] = context.TraceId,
            ["context"] = new Dictionary<string, object?>
            {
                ["route"] = context.Route,
                ["intent_type"] = context.IntentType,
                ["query_classification"] = context.QueryClassification,
                ["presentation_template_family"] = context.PresentationTemplateFamily,
                ["chat_response_profile"] = context.ChatResponseProfile,
                ["context_awareness_mode"] = context.ContextAwarenessMode,
                ["evidence_required"] = context.EvidenceRequired,
                ["trusted_web_context_requested"] = context.TrustedWebContextRequested,
                ["trusted_web_context_enabled"] = context.TrustedWebContextEnabled,
                ["home_node_id_present"] = !string.IsNullOrWhiteSpace(context.HomeNodeId),
                ["home_node_execution_mode"] = context.HomeNodeExecutionMode,
                ["execution_state"] = context.ExecutionState,
                ["mcp_executed"] = context.McpExecuted,
                ["mcp_policy_decision"] = context.McpPolicyDecision,
                ["counts"] = new Dictionary<string, object?>
                {
                    ["memory_results"] = context.MemoryResults.Count,
                    ["source_results"] = context.SourceResults.Count,
                    ["graph_evidence"] = context.GraphEvidence.Count,
                    ["retrieved"] = context.Retrieved.Count,
                    ["home_node_sources"] = context.HomeNodeSources.Count,
                    ["trusted_web_results"] = context.TrustedWebResults.Count
                }
            },
            ["response"] = new Dictionary<string, object?>
            {
                ["answer"] = response.Answer,
                ["confidence"] = response.Confidence,
                ["execution_state"] = response.ExecutionState,
                ["execution_target"] = response.ExecutionTarget,
                ["source_count"] = response.Sources?.Count ?? 0,
                ["trace"] = new Dictionary<string, object?>
                {
                    ["route"] = response.Trace.Route,
                    ["policy_mode"] = response.Trace.PolicyMode,
                    ["model"] = response.Trace.Model,
                    ["pack_hash"] = response.Trace.PackHash,
                    ["trace_id"] = response.Trace.TraceId
                }
            }
        };
    }

    private static string HashUserId(string? userId)
    {
        var source = string.IsNullOrWhiteSpace(userId) ? "unknown" : userId.Trim();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private sealed class RuntimeCriticHttpResponse
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("mode")]
        public string? Mode { get; set; }

        [JsonPropertyName("overall_score")]
        public double OverallScore { get; set; } = 1.0d;

        [JsonPropertyName("scores")]
        public List<RuntimeCriticHttpScore>? Scores { get; set; }

        [JsonPropertyName("findings")]
        public List<RuntimeCriticHttpFinding>? Findings { get; set; }

        public RuntimeCriticResult ToResult()
        {
            return new RuntimeCriticResult(
                string.IsNullOrWhiteSpace(Version) ? "runtime-critic-external-v1" : Version,
                string.IsNullOrWhiteSpace(Mode) ? "observe_only" : Mode,
                OverallScore,
                Scores?.Select(score => new RuntimeCriticDimensionScore(
                    score.Dimension ?? "unknown",
                    score.Score,
                    score.Reason ?? "unknown")).ToList() ?? new List<RuntimeCriticDimensionScore>(),
                Findings?.Select(finding => new RuntimeCriticFinding(
                    finding.Severity ?? "info",
                    finding.Category ?? "unknown",
                    finding.Rule ?? "unknown",
                    finding.Detail ?? string.Empty)).ToList() ?? new List<RuntimeCriticFinding>());
        }
    }

    private sealed class RuntimeCriticHttpScore
    {
        [JsonPropertyName("dimension")]
        public string? Dimension { get; set; }

        [JsonPropertyName("score")]
        public double Score { get; set; } = 1.0d;

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }

    private sealed class RuntimeCriticHttpFinding
    {
        [JsonPropertyName("severity")]
        public string? Severity { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("rule")]
        public string? Rule { get; set; }

        [JsonPropertyName("detail")]
        public string? Detail { get; set; }
    }
}
#nullable disable
