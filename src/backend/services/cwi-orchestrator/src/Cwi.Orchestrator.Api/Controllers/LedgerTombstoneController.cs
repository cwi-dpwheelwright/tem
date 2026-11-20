#nullable enable
using Cwi.Orchestrator.Api.BreakGlass;
using Cwi.Orchestrator.Application.Provenance;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/ledger/tombstone")]
public sealed class LedgerTombstoneController : ControllerBase
{
    private readonly LedgerTombstoneService _service;

    public LedgerTombstoneController(LedgerTombstoneService service)
    {
        _service = service;
    }

    [HttpPost]
    [BreakGlassScope("admin")]
    public async Task<IActionResult> Tombstone(
        [FromBody] LedgerTombstoneRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TraceId))
        {
            return BadRequest(new { error = "trace_id required" });
        }

        await _service.TombstoneAsync(request.TraceId, request.Reason ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        return Accepted(new { trace_id = request.TraceId });
    }
}
#nullable disable
