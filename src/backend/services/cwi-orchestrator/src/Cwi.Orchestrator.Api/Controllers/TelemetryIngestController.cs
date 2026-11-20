#nullable enable
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Health;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Telemetry;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("mcp/ingest/telemetry")]
[Route("api/ingest/telemetry")]
public sealed class TelemetryIngestController : ControllerBase
{
    private readonly TelemetryIngestService _telemetryIngestService;
    private readonly DependencyHealthChecker _healthChecker;

    public TelemetryIngestController(
        TelemetryIngestService telemetryIngestService,
        DependencyHealthChecker healthChecker)
    {
        _telemetryIngestService = telemetryIngestService;
        _healthChecker = healthChecker;
    }

    [HttpPost]
    public async Task<ActionResult<TelemetryIngestResponseDto>> Ingest(
        [FromBody] TelemetryIngestRequestDto request,
        CancellationToken cancellationToken)
    {
        var report = await _healthChecker.CheckAsync(cancellationToken).ConfigureAwait(false);
        if (!report.IsHealthy)
        {
            return StatusCode(503, new DomainError(ErrorCodes.DependencyUnavailable, "Dependencies unavailable."));
        }

        var result = await _telemetryIngestService.HandleAsync(request, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, result.Error);
        }

        return Ok(result.Data);
    }
}
#nullable disable
