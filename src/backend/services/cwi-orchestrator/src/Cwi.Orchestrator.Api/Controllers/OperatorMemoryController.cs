#nullable enable
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.OperatorMemory;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/operator-memory")]
public sealed class OperatorMemoryController : ControllerBase
{
    private readonly OperatorMemoryIngestService _ingestService;
    private readonly OperatorProfileService _profileService;

    public OperatorMemoryController(
        OperatorMemoryIngestService ingestService,
        OperatorProfileService profileService)
    {
        _ingestService = ingestService;
        _profileService = profileService;
    }

    [HttpPost("ingest")]
    public async Task<ActionResult<OperatorMemoryIngestResponseDto>> Ingest(
        [FromBody] OperatorMemoryIngestRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _ingestService.HandleAsync(request, cancellationToken).ConfigureAwait(false);
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

    [HttpGet("profile")]
    public async Task<ActionResult<OperatorProfileSummaryDto>> GetProfile(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "operator_id")] string? operatorId,
        CancellationToken cancellationToken)
    {
        var resolvedTenant = ResolveTenantId(tenantId);
        var resolvedOperator = string.IsNullOrWhiteSpace(operatorId) ? null : operatorId.Trim();

        var result = await _profileService
            .GetAsync(resolvedTenant ?? string.Empty, resolvedOperator ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

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

    private string? ResolveTenantId(string? tenantId)
    {
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            return tenantId.Trim();
        }

        var header = Request.Headers["x-tenant-id"].ToString();
        return string.IsNullOrWhiteSpace(header) ? null : header.Trim();
    }
}
#nullable disable
