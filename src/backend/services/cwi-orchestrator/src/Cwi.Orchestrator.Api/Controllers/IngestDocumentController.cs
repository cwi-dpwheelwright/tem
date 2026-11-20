#nullable enable
using System.Security.Claims;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Api.Health;
using Cwi.Orchestrator.Application.Etl;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/ingest/document")]
public sealed class IngestDocumentController : ControllerBase
{
    private readonly IngestDocumentService _ingestDocumentService;
    private readonly DependencyHealthChecker _healthChecker;

    public IngestDocumentController(
        IngestDocumentService ingestDocumentService,
        DependencyHealthChecker healthChecker)
    {
        _ingestDocumentService = ingestDocumentService;
        _healthChecker = healthChecker;
    }

    [HttpPost]
    public async Task<ActionResult<IngestDocumentResponseDto>> Ingest(
        [FromBody] IngestDocumentRequestDto request,
        CancellationToken cancellationToken)
    {
        var normalizedRequest = NormalizeRequest(request);
        if (normalizedRequest is null)
        {
            return StatusCode(403, new DomainError(ErrorCodes.Forbidden, "Token identity does not match ingest payload."));
        }

        var report = await _healthChecker.CheckAsync(cancellationToken).ConfigureAwait(false);
        if (!report.IsHealthy)
        {
            return StatusCode(503, new DomainError(ErrorCodes.DependencyUnavailable, "Dependencies unavailable."));
        }

        var result = await _ingestDocumentService.HandleAsync(normalizedRequest, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, result.Error);
        }

        return Ok(result.Data);
    }

    private IngestDocumentRequestDto? NormalizeRequest(IngestDocumentRequestDto request)
    {
        var claimTenantId = User.FindFirst("tenant_id")?.Value?.Trim();
        var claimUserId = User.FindFirst("sub")?.Value?.Trim()
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim();
        var requestTenantId = request.TenantId?.Trim();
        var requestUserId = request.UserId?.Trim();

        if (!string.IsNullOrWhiteSpace(claimUserId) &&
            !string.IsNullOrWhiteSpace(requestUserId) &&
            !string.Equals(claimUserId, requestUserId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var resolvedTenantId = !string.IsNullOrWhiteSpace(claimTenantId)
            ? claimTenantId
            : requestTenantId;
        var resolvedUserId = !string.IsNullOrWhiteSpace(claimUserId)
            ? claimUserId
            : requestUserId;

        if (string.IsNullOrWhiteSpace(resolvedTenantId) || string.IsNullOrWhiteSpace(resolvedUserId))
        {
            return null;
        }

        request.TenantId = resolvedTenantId.Trim();
        request.UserId = resolvedUserId.Trim();
        return request;
    }
}
#nullable disable
