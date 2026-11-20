using Microsoft.AspNetCore.Mvc;
using Cwi.Router.Application.Metrics;

namespace Cwi.Router.Api.Controllers;

[ApiController]
[Route("api/metrics")]
public sealed class MetricsController : ControllerBase
{
    private static readonly DateTimeOffset StartUtc = DateTimeOffset.UtcNow;
    private readonly RouterMetrics _metrics;

    public MetricsController(RouterMetrics metrics)
    {
        _metrics = metrics;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var uptimeSeconds = Math.Max(0, (DateTimeOffset.UtcNow - StartUtc).TotalSeconds);
        var (allowed, blocked) = _metrics.SnapshotSurvivalPriority();
        var body = string.Join('\n', new[]
        {
            "# HELP cwi_router_uptime_seconds Uptime in seconds.",
            "# TYPE cwi_router_uptime_seconds gauge",
            $"cwi_router_uptime_seconds {uptimeSeconds:F0}",
            "# HELP cwi_router_metrics_configured Whether metrics endpoint is configured (1=yes).",
            "# TYPE cwi_router_metrics_configured gauge",
            "cwi_router_metrics_configured 1",
            "# HELP cwi_router_survival_priority_allowed_total Allowed requests during survival priority gating.",
            "# TYPE cwi_router_survival_priority_allowed_total counter",
            $"cwi_router_survival_priority_allowed_total {allowed}",
            "# HELP cwi_router_survival_priority_blocked_total Blocked requests during survival priority gating.",
            "# TYPE cwi_router_survival_priority_blocked_total counter",
            $"cwi_router_survival_priority_blocked_total {blocked}"
        });

        return Content(body, "text/plain; version=0.0.4");
    }
}
