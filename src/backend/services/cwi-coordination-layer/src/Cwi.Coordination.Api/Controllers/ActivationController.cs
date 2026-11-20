using Cwi.Coordination.Api.Infrastructure;
using Cwi.Coordination.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Coordination.Api.Controllers;

[ApiController]
[Route("api/activation")]
public sealed class ActivationController : ControllerBase
{
    private readonly ActivationStatusService _service;

    public ActivationController(ActivationStatusService service)
    {
        _service = service;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(
        [FromQuery(Name = "node_id")] string? nodeId,
        CancellationToken cancellationToken)
    {
        var response = await _service.GetStatusAsync(nodeId, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }
}
