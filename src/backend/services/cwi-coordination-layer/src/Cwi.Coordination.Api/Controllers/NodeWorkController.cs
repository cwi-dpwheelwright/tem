using Cwi.Common.Errors;
using Cwi.Coordination.Api.Infrastructure;
using Cwi.Coordination.Api.Security;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Coordination.Api.Controllers;

[ApiController]
[Route("api/node-work")]
public sealed class NodeWorkController : ControllerBase
{
    private readonly NodeWorkService _service;
    private readonly AdminAuthorizationService _adminAuthorization;

    public NodeWorkController(NodeWorkService service, AdminAuthorizationService adminAuthorization)
    {
        _service = service;
        _adminAuthorization = adminAuthorization;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateNodeWorkRequest request, CancellationToken cancellationToken)
    {
        if (!_adminAuthorization.TryAuthorize(HttpContext, out var error))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new DomainError(ErrorCodes.Forbidden, error ?? "forbidden"));
        }

        var response = await _service.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "node_id")] string? nodeId,
        [FromQuery(Name = "status")] string? status,
        CancellationToken cancellationToken)
    {
        if (!_adminAuthorization.TryAuthorize(HttpContext, out var error))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new DomainError(ErrorCodes.Forbidden, error ?? "forbidden"));
        }

        var response = await _service.ListAsync(tenantId, nodeId, status, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpGet("{workId}")]
    public async Task<IActionResult> Get(
        [FromRoute] string workId,
        CancellationToken cancellationToken)
    {
        if (!_adminAuthorization.TryAuthorize(HttpContext, out var error))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new DomainError(ErrorCodes.Forbidden, error ?? "forbidden"));
        }

        var response = await _service.GetAsync(workId, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpPost("lease")]
    public async Task<IActionResult> Lease([FromBody] LeaseNodeWorkRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.LeaseAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpPost("{workId}/ack")]
    public async Task<IActionResult> Ack(
        [FromRoute] string workId,
        [FromBody] AckNodeWorkRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.AcknowledgeAsync(workId, request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpPost("{workId}/result")]
    public async Task<IActionResult> Result(
        [FromRoute] string workId,
        [FromBody] CompleteNodeWorkRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.CompleteAsync(workId, request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }
}
