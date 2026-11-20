using Cwi.Coordination.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Coordination.Api.Controllers;

[ApiController]
[Route("api/ledger")]
public sealed class LedgerController : ControllerBase
{
    private readonly LedgerQueryService _ledger;
    private readonly LedgerVerificationService _verification;

    public LedgerController(LedgerQueryService ledger, LedgerVerificationService verification)
    {
        _ledger = ledger;
        _verification = verification;
    }

    [HttpGet("events")]
    public async Task<IActionResult> Events([FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        var events = await _ledger.ListAsync(limit, cancellationToken).ConfigureAwait(false);
        return Ok(events);
    }

    [HttpGet("verify")]
    public async Task<IActionResult> Verify(
        [FromQuery] int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        var result = await _verification.VerifyAsync(limit, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }
}
