using Cwi.Coordination.Api.Infrastructure;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Coordination.Api.Controllers;

[ApiController]
[Route("api/degradation")]
public sealed class DegradationEventsController : ControllerBase
{
    private readonly DegradationEventService _service;

    public DegradationEventsController(DegradationEventService service)
    {
        _service = service;
    }

    [HttpPost("events")]
    public async Task<IActionResult> Append(
        [FromBody] DegradationEventRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.AppendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }
}
