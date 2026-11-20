using Cwi.Coordination.Api.Infrastructure;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Coordination.Api.Controllers;

[ApiController]
[Route("api/transport")]
public sealed class TransportController : ControllerBase
{
    private readonly LinkMetricsIngestService _service;

    public TransportController(LinkMetricsIngestService service)
    {
        _service = service;
    }

    [HttpPost("link-metrics")]
    public async Task<IActionResult> Ingest(
        [FromBody] LinkMetricsTransportRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.IngestAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }
}
