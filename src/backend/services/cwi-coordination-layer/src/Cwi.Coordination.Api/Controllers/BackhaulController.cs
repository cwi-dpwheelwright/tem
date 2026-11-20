using Cwi.Coordination.Api.Infrastructure;
using Cwi.Coordination.Api.Security;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Services;
using Cwi.Common.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Coordination.Api.Controllers;

[ApiController]
[Route("api/backhaul")]
public sealed class BackhaulController : ControllerBase
{
    private readonly BackhaulPolicyService _service;
    private readonly AdminAuthorizationService _adminAuthorization;

    public BackhaulController(BackhaulPolicyService service, AdminAuthorizationService adminAuthorization)
    {
        _service = service;
        _adminAuthorization = adminAuthorization;
    }

    [HttpGet("policy")]
    public async Task<IActionResult> GetPolicy(CancellationToken cancellationToken)
    {
        var response = await _service.GetPolicyAsync(cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpPut("policy")]
    public async Task<IActionResult> SetPolicy([FromBody] BackhaulPolicyRequest request, CancellationToken cancellationToken)
    {
        if (!_adminAuthorization.TryAuthorize(HttpContext, out var error))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new DomainError(ErrorCodes.Forbidden, error ?? "forbidden"));
        }

        var response = await _service.SetPolicyAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpPost("evaluate")]
    public async Task<IActionResult> Evaluate([FromBody] BackhaulDecisionRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }
}
