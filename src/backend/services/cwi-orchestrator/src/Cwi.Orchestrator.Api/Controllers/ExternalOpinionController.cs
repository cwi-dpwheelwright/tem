#nullable enable
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Runtime;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/admin/external-opinion/evaluations")]
public sealed class ExternalOpinionController : ControllerBase
{
    private readonly ExternalOpinionService _service;

    public ExternalOpinionController(ExternalOpinionService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<IActionResult> Evaluate(
        [FromBody] ExternalOpinionRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!AdminEntitlementEvaluator.HasAdminEntitlement(User))
        {
            return StatusCode(403, new DomainError(ErrorCodes.Forbidden, "Admin entitlement required."));
        }

        var result = await _service.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return StatusCode(result.StatusCode, new
            {
                error = result.Error?.Code ?? "external_opinion_failed",
                message = result.Error?.Message ?? result.Message
            });
        }

        return StatusCode(result.StatusCode, result.Data);
    }
}
#nullable disable
