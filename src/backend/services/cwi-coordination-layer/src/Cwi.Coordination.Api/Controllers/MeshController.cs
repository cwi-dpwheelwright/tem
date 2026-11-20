using Cwi.Coordination.Api.Infrastructure;
using Cwi.Coordination.Api.Security;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Services;
using Cwi.Common.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Coordination.Api.Controllers;

[ApiController]
[Route("api/mesh")]
public sealed class MeshController : ControllerBase
{
    private readonly MeshTopologyService _topologyService;
    private readonly RoutingHintService _routingHintService;
    private readonly TopologyHealthService _healthService;
    private readonly RerouteGuidanceService _rerouteService;
    private readonly AdminAuthorizationService _adminAuthorization;

    public MeshController(
        MeshTopologyService topologyService,
        RoutingHintService routingHintService,
        TopologyHealthService healthService,
        RerouteGuidanceService rerouteService,
        AdminAuthorizationService adminAuthorization)
    {
        _topologyService = topologyService;
        _routingHintService = routingHintService;
        _healthService = healthService;
        _rerouteService = rerouteService;
        _adminAuthorization = adminAuthorization;
    }

    [HttpPost("links")]
    public async Task<IActionResult> UpsertLink([FromBody] LinkStateUpdateRequest request, CancellationToken cancellationToken)
    {
        var response = await _topologyService.UpsertAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpGet("links")]
    public async Task<IActionResult> ListLinks([FromQuery(Name = "node_id")] string? nodeId, CancellationToken cancellationToken)
    {
        var response = await _topologyService.ListAsync(nodeId, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpGet("events")]
    public async Task<IActionResult> Events(
        [FromQuery] int? limit = null,
        [FromQuery(Name = "since_utc")] DateTimeOffset? sinceUtc = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _topologyService.ListEventsAsync(limit ?? 0, sinceUtc, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        var response = await _healthService.GetHealthAsync(cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpPost("hints")]
    public async Task<IActionResult> Hint([FromBody] RoutingHintRequest request, CancellationToken cancellationToken)
    {
        var response = await _routingHintService.GetHintAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }

    [HttpPost("reroute")]
    public async Task<IActionResult> Reroute([FromBody] RerouteGuidanceRequest request, CancellationToken cancellationToken)
    {
        if (!_adminAuthorization.TryAuthorize(HttpContext, out var error))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new DomainError(ErrorCodes.Forbidden, error ?? "forbidden"));
        }

        var response = await _rerouteService.GetGuidanceAsync(request, cancellationToken).ConfigureAwait(false);
        return response.ToActionResult();
    }
}
