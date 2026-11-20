using Cwi.Orchestrator.Application.BreakGlass;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/break-glass")]
public sealed class BreakGlassController : ControllerBase
{
    private readonly BreakGlassService _service;
    private readonly BreakGlassAuditService _audit;

    public BreakGlassController(BreakGlassService service, BreakGlassAuditService audit)
    {
        _service = service;
        _audit = audit;
    }

    [HttpPost("issue")]
    public async Task<ActionResult<BreakGlassIssueResponseDto>> Issue(
        [FromBody] BreakGlassIssueRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _service.IssueAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, result.Error);
        }

        return Ok(result.Data);
    }

    [HttpPost("revoke")]
    public async Task<ActionResult<BreakGlassRevokeResponseDto>> Revoke(
        [FromBody] BreakGlassRevokeRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _service.RevokeAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, result.Error);
        }

        return Ok(result.Data);
    }

    [HttpGet("audit")]
    public async Task<ActionResult<BreakGlassAuditPage>> Audit(
        [FromQuery(Name = "tenant_id")] string tenantId,
        [FromQuery(Name = "page")] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _audit.QueryAsync(tenantId, page, pageSize, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, result.Error);
        }

        return Ok(result.Data);
    }
}
