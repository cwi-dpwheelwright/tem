#nullable enable
using Cwi.Orchestrator.Application.Telemetry;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/telemetry/index")]
public sealed class TelemetryIndexController : ControllerBase
{
    private const int MaxLimit = 200;

    private readonly TelemetryIndexStore _store;
    private readonly TelemetryIndexOptions _options;

    public TelemetryIndexController(TelemetryIndexStore store, TelemetryIndexOptions options)
    {
        _store = store;
        _options = options;
    }

    [HttpGet]
    public ActionResult<TelemetryIndexPage> Get(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "limit")] int limit = 100)
    {
        var resolvedLimit = limit < 1 ? 1 : Math.Min(limit, MaxLimit);
        var items = _store.GetLatest(tenantId, resolvedLimit);

        var payload = new TelemetryIndexPage
        {
            Enabled = _options.Enabled,
            Limit = resolvedLimit,
            Items = items
        };

        return Ok(payload);
    }
}
#nullable disable
