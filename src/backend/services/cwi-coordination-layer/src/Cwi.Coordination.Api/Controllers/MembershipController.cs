using Cwi.Coordination.Api.Infrastructure;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Coordination.Api.Controllers;

[ApiController]
[Route("api/membership")]
public sealed class MembershipController : ControllerBase
{
    private readonly IMembershipService _service;

    public MembershipController(IMembershipService service)
    {
        _service = service;
    }

    [HttpPost("enroll")]
    public async Task<IActionResult> Enroll([FromBody] EnrollmentRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.EnrollAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpPost("rotate")]
    public async Task<IActionResult> Rotate([FromBody] RotationRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.RotateAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke([FromBody] RevokeRequest request, CancellationToken cancellationToken)
    {
        var response = await _service.RevokeAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var response = await _service.ListAsync(cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }
}
