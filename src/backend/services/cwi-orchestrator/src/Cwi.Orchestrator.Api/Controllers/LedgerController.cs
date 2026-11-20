using Cwi.Orchestrator.Application.Provenance;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/ledger")]
public sealed class LedgerController : ControllerBase
{
    private readonly LedgerVerificationService _verificationService;
    private readonly LedgerExportService _exportService;
    private readonly LedgerEventQueryService _queryService;

    public LedgerController(
        LedgerVerificationService verificationService,
        LedgerExportService exportService,
        LedgerEventQueryService queryService)
    {
        _verificationService = verificationService;
        _exportService = exportService;
        _queryService = queryService;
    }

    [HttpGet("verify")]
    public async Task<ActionResult<LedgerVerificationResult>> Verify(
        [FromQuery(Name = "trace_id")] string traceId,
        CancellationToken cancellationToken)
    {
        var result = await _verificationService.VerifyAsync(traceId, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpGet("export")]
    public async Task<ActionResult<LedgerExportPage>> Export(
        [FromQuery(Name = "trace_id")] string traceId,
        [FromQuery(Name = "page")] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _exportService.ExportAsync(traceId, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpGet("events")]
    public async Task<ActionResult<LedgerEventPage>> Events(
        [FromQuery(Name = "start_utc")] DateTimeOffset? startUtc,
        [FromQuery(Name = "end_utc")] DateTimeOffset? endUtc,
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "event_type")] string? eventType,
        [FromQuery(Name = "page")] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _queryService
            .QueryAsync(startUtc, endUtc, tenantId, eventType, page, pageSize, cancellationToken)
            .ConfigureAwait(false);
        return Ok(result);
    }
}
