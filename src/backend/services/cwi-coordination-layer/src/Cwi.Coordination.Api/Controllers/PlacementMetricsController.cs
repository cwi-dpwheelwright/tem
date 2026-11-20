using Cwi.Coordination.Api.Infrastructure;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Coordination.Api.Controllers;

[ApiController]
[Route("api/placement/metrics")]
public sealed class PlacementMetricsController : ControllerBase
{
    private readonly PlacementMetricsService _service;

    public PlacementMetricsController(PlacementMetricsService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<IActionResult> Ingest(
        [FromBody] PlacementMetricsRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.IngestAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery(Name = "node_id")] string? nodeId,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var response = await _service.ListAsync(nodeId, limit, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }
}
