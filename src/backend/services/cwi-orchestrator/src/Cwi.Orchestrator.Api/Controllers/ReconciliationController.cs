#nullable enable
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Reconciliation;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/reconciliation/records")]
public sealed class ReconciliationController : ControllerBase
{
    private readonly CanonicalRecordService _service;

    public ReconciliationController(CanonicalRecordService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<ActionResult<CanonicalRecordResponseDto>> Upsert(
        [FromBody] CanonicalRecordUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _service.UpsertAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, new
            {
                error = result.Error?.Code ?? "invalid_request",
                message = result.Error?.Message ?? result.Message
            });
        }

        return Ok(result.Data);
    }

    [HttpGet("{recordId}")]
    public async Task<ActionResult<CanonicalRecordResponseDto>> Get(
        [FromRoute] string recordId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(recordId, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, new
            {
                error = result.Error?.Code ?? "invalid_request",
                message = result.Error?.Message ?? result.Message
            });
        }

        return Ok(result.Data);
    }

    [HttpGet("{recordId}/report")]
    public async Task<ActionResult<ReconciliationReportDto>> Report(
        [FromRoute] string recordId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetReportAsync(recordId, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, new
            {
                error = result.Error?.Code ?? "invalid_request",
                message = result.Error?.Message ?? result.Message
            });
        }

        return Ok(result.Data);
    }
}
#nullable disable
