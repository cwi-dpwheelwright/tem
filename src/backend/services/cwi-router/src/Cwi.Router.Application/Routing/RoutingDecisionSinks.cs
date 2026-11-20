using System.Net.Http.Json;
using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.Pipeline;
using Microsoft.Extensions.Logging;

namespace Cwi.Router.Application.Routing;

public interface IRoutingDecisionSink
{
    Task RecordAsync(RoutingDecision decision, RouterRequestContext context, CancellationToken cancellationToken);
}

public sealed class RoutingDecisionLogger : IRoutingDecisionSink
{
    private readonly ILogger<RoutingDecisionLogger> _logger;

    public RoutingDecisionLogger(ILogger<RoutingDecisionLogger> logger)
    {
        _logger = logger;
    }

    public Task RecordAsync(RoutingDecision decision, RouterRequestContext context, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "routing decision id={DecisionId} tier={Tier} fallback={Fallback} complexity={Complexity} score={Score} survival={SurvivalMode} network_status={NetworkStatus} network_score={NetworkScore} reroute_required={RerouteRequired} reasons={Reasons} trace_id={TraceId} tenant_id={TenantId}",
            decision.DecisionId,
            decision.SelectedTier,
            decision.FallbackUsed,
            decision.Complexity.Level,
            decision.Score,
            decision.SurvivalMode,
            decision.NetworkAssessment?.Status ?? "unknown",
            decision.NetworkAssessment?.Score,
            decision.RerouteRequired,
            string.Join(',', decision.ReasonCodes),
            context.HttpContext.TraceIdentifier,
            context.TenantId ?? "unknown");
        return Task.CompletedTask;
    }
}

public sealed class RoutingDecisionTelemetrySink : IRoutingDecisionSink
{
    private readonly RoutingDecisionOptions _options;
    private readonly HttpClient _client;
    private readonly RouterConfig _config;

    public RoutingDecisionTelemetrySink(
        RoutingDecisionOptions options,
        IHttpClientFactory httpClientFactory,
        RouterConfig config)
    {
        _options = options;
        _client = httpClientFactory.CreateClient("orchestrator");
        _config = config;
    }

    public async Task RecordAsync(RoutingDecision decision, RouterRequestContext context, CancellationToken cancellationToken)
    {
        if (!_options.EmitTelemetry)
        {
            return;
        }

        var tenantId = context.TenantId ?? _options.TelemetryTenantFallback;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        var traceId = context.HttpContext.TraceIdentifier ?? string.Empty;
        var payload = new
        {
            tenant_id = tenantId,
            window = new
            {
                start = DateTimeOffset.UtcNow,
                end = DateTimeOffset.UtcNow
            },
            signals = new[]
            {
                new { tag = "routing_decision", unit = "count", series = new [] { 1.0f } }
            },
            metadata = RoutingDecisionEvent.FromDecision(decision, traceId, tenantId)
        };

        try
        {
            var uri = new Uri(new Uri(_config.OrchestratorUrl), "/mcp/ingest/telemetry");
            using var response = await _client.PostAsJsonAsync(uri, payload, cancellationToken).ConfigureAwait(false);
            _ = response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
        }
    }
}
