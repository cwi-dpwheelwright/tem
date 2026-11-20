#nullable enable
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Reports;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/reports")]
public sealed class ReportsController : ControllerBase
{
    private readonly ReportStore _store;

    public ReportsController(ReportStore store)
    {
        _store = store;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<ReportSummaryDto>> List([FromQuery(Name = "tenant_id")] string? tenantId)
    {
        var resolvedTenant = ResolveTenantId(tenantId);
        if (string.IsNullOrWhiteSpace(resolvedTenant))
        {
            return BadRequest(new { error = "invalid_request", message = "tenant_id required" });
        }

        var reports = _store.List(resolvedTenant);
        return Ok(reports);
    }

    [HttpGet("{reportId}")]
    public ActionResult<ReportDetailDto> Get(
        string reportId,
        [FromQuery(Name = "tenant_id")] string? tenantId)
    {
        var resolvedTenant = ResolveTenantId(tenantId);
        if (string.IsNullOrWhiteSpace(resolvedTenant))
        {
            return BadRequest(new { error = "invalid_request", message = "tenant_id required" });
        }

        var report = _store.Get(resolvedTenant, reportId);
        if (report is null)
        {
            return NotFound(new { error = "not_found", message = "report not found" });
        }

        return Ok(report);
    }

    private string? ResolveTenantId(string? tenantId)
    {
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            return tenantId.Trim();
        }

        var header = Request.Headers["x-tenant-id"].ToString();
        return string.IsNullOrWhiteSpace(header) ? null : header.Trim();
    }
}
#nullable disable
