using Cwi.Coordination.Api.Infrastructure;
using Cwi.Coordination.Api.Security;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Services;
using Cwi.Common.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Coordination.Api.Controllers;

[ApiController]
[Route("api/survival")]
public sealed class SurvivalController : ControllerBase
{
    private readonly SurvivalModeService _service;
    private readonly AdminAuthorizationService _adminAuthorization;

    public SurvivalController(SurvivalModeService service, AdminAuthorizationService adminAuthorization)
    {
        _service = service;
        _adminAuthorization = adminAuthorization;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var response = await _service.GetAsync(cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpPost]
    public async Task<IActionResult> Set([FromBody] SurvivalModeRequest request, CancellationToken cancellationToken)
    {
        if (!_adminAuthorization.TryAuthorize(HttpContext, out var error))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new DomainError(ErrorCodes.Forbidden, error ?? "forbidden"));
        }

        var response = await _service.SetAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }
}
