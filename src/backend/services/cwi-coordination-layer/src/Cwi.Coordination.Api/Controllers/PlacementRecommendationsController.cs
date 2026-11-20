using Cwi.Coordination.Api.Infrastructure;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Coordination.Api.Controllers;

[ApiController]
[Route("api/placement/recommendations")]
public sealed class PlacementRecommendationsController : ControllerBase
{
    private readonly PlacementRecommendationService _service;

    public PlacementRecommendationsController(PlacementRecommendationService service)
    {
        _service = service;
    }

    [HttpPost]
    public async Task<IActionResult> Recommend(
        [FromBody] PlacementRecommendationRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _service.RecommendAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }
}
