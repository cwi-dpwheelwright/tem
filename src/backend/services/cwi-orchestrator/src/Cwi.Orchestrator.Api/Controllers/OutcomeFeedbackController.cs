using System.Security.Claims;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Feedback;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/feedback")]
public sealed class OutcomeFeedbackController : ControllerBase
{
    private readonly IOutcomeFeedbackService _service;
    private readonly FeedbackReportService _reportService;

    public OutcomeFeedbackController(IOutcomeFeedbackService service, FeedbackReportService reportService)
    {
        _service = service;
        _reportService = reportService;
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Record([FromBody] OutcomeFeedbackRequestDto request, CancellationToken cancellationToken)
    {
        var result = await _service.RecordAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return StatusCode(result.StatusCode, new
            {
                error = result.Error?.Code ?? "invalid_request",
                message = result.Error?.Message ?? result.Message
            });
        }

        return Ok(result.Data);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(
        [FromQuery(Name = "tenant_id")] string tenantId,
        [FromQuery(Name = "trace_id")] string traceId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetSummaryAsync(tenantId, traceId, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return StatusCode(result.StatusCode, new
            {
                error = result.Error?.Code ?? "invalid_request",
                message = result.Error?.Message ?? result.Message
            });
        }

        return Ok(result.Data);
    }

    [HttpPost("report")]
    [JwtRequired]
    [Consumes("application/json")]
    public async Task<IActionResult> Report([FromBody] FeedbackReportRequestDto request, CancellationToken cancellationToken)
    {
        var tenantId = User.FindFirst("tenant_id")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(tenantId)
            && !string.IsNullOrWhiteSpace(request.TenantId)
            && !string.Equals(tenantId, request.TenantId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(403, new DomainError(ErrorCodes.Forbidden, "Tenant mismatch."));
        }

        tenantId = string.IsNullOrWhiteSpace(tenantId) ? request.TenantId?.Trim() : tenantId;

        var userId = User.FindFirst("sub")?.Value?.Trim()
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(userId)
            && !string.IsNullOrWhiteSpace(request.UserId)
            && !string.Equals(userId, request.UserId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(403, new DomainError(ErrorCodes.Forbidden, "User mismatch."));
        }

        userId = string.IsNullOrWhiteSpace(userId) ? request.UserId?.Trim() : userId;

        var result = await _reportService
            .SubmitAsync(request, tenantId ?? string.Empty, userId ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return StatusCode(result.StatusCode, new
            {
                ok = false,
                error = result.Error?.Code ?? "feedback_submit_failed",
                message = result.Error?.Message ?? result.Message
            });
        }

        return Ok(result.Data);
    }
}
