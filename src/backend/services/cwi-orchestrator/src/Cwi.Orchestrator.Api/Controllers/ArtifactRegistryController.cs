#nullable enable
using Cwi.Orchestrator.Api.BreakGlass;
using Cwi.Orchestrator.Application.Artifacts;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/artifacts")]
public sealed class ArtifactRegistryController : ControllerBase
{
    private readonly ArtifactRegistryService _service;

    public ArtifactRegistryController(ArtifactRegistryService service)
    {
        _service = service;
    }

    [HttpPost("register")]
    [Consumes("application/json")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<ArtifactRegistrationResponseDto>> Register(
        [FromBody] ArtifactRegistrationRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _service.RegisterAsync(request, cancellationToken).ConfigureAwait(false);
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

    [HttpGet]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<IReadOnlyList<ArtifactSummaryDto>>> List(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "artifact_type")] string? artifactType,
        CancellationToken cancellationToken)
    {
        var resolvedTenant = ResolveTenantId(tenantId);
        var result = await _service
            .ListAsync(resolvedTenant ?? string.Empty, artifactType, cancellationToken)
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

    [HttpGet("{artifactId}")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<ArtifactDetailDto>> Get(
        string artifactId,
        [FromQuery(Name = "tenant_id")] string? tenantId,
        CancellationToken cancellationToken)
    {
        var resolvedTenant = ResolveTenantId(tenantId);
        var result = await _service
            .GetAsync(resolvedTenant ?? string.Empty, artifactId, cancellationToken)
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
