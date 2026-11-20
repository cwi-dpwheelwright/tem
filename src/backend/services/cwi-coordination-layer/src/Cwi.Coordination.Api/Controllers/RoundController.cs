using Cwi.Coordination.Api.Infrastructure;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Coordination.Api.Controllers;

[ApiController]
[Route("api/rounds")]
public sealed class RoundController : ControllerBase
{
    private readonly RoundService _service;

    public RoundController(RoundService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] RoundCreateRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var response = await _service.ListAsync(cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpPost("{roundId:guid}/heartbeat")]
    public async Task<IActionResult> Heartbeat(
        [FromRoute] Guid roundId,
        [FromBody] HeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.HeartbeatAsync(roundId, request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }
}
