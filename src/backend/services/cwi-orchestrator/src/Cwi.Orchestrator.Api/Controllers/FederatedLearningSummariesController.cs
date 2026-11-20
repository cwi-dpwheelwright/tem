#nullable enable
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Federated;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/admin/federated-learning/summaries")]
public sealed class FederatedLearningSummariesController : ControllerBase
{
    private readonly FederatedLearningSummaryService _service;

    public FederatedLearningSummariesController(FederatedLearningSummaryService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<IActionResult> Submit(
        [FromBody] FederatedLearningSummaryRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!AdminEntitlementEvaluator.HasAdminEntitlement(User))
        {
            return StatusCode(403, new DomainError(ErrorCodes.Forbidden, "Admin entitlement required."));
        }

        var result = await _service.SubmitAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return StatusCode(result.StatusCode, new
            {
                error = result.Error?.Code ?? "federated_learning_summary_failed",
                message = result.Error?.Message ?? result.Message
            });
        }

        return StatusCode(result.StatusCode, result.Data);
    }
}
#nullable disable
