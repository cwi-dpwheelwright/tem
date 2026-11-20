#nullable enable
using System.Text.Json;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Recursive;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/recursive/events")]
public sealed class RecursiveEventsController : ControllerBase
{
    private readonly RecursiveEventIngestService _service;

    public RecursiveEventsController(RecursiveEventIngestService service)
    {
        _service = service;
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Ingest(
        [FromBody] JsonElement request,
        CancellationToken cancellationToken)
    {
        if (!AdminEntitlementEvaluator.HasAdminEntitlement(User))
        {
            return StatusCode(403, new DomainError(ErrorCodes.Forbidden, "Admin entitlement required."));
        }

        var result = await _service.IngestAsync(request.GetRawText(), cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return StatusCode(result.StatusCode, new
            {
                error = result.Error?.Code ?? "recursive_event_ingest_failed",
                message = result.Error?.Message ?? result.Message
            });
        }

        return StatusCode(result.StatusCode, result.Data);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RecursiveEventRecordDto>>> Query(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "event_type")] string? eventType,
        [FromQuery(Name = "temporal_state")] string? temporalState,
        [FromQuery(Name = "page")] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!AdminEntitlementEvaluator.HasAdminEntitlement(User))
        {
            return StatusCode(403, new DomainError(ErrorCodes.Forbidden, "Admin entitlement required."));
        }

        var result = await _service
            .QueryAsync(tenantId, eventType, temporalState, page, pageSize, cancellationToken)
            .ConfigureAwait(false);

        return Ok(result);
    }
}
#nullable disable
