using Cwi.Router.Api.Models;
using Cwi.Router.Application.Journey;
using Cwi.Router.Application.Pipeline;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text;

namespace Cwi.Router.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class ProxyController : ControllerBase
{
    private readonly RouterPipeline _pipeline;
    private readonly JourneyRequestContext _journeyContext;
    private readonly RouterJourneyEmitter _journeyEmitter;

    public ProxyController(
        RouterPipeline pipeline,
        JourneyRequestContext journeyContext,
        RouterJourneyEmitter journeyEmitter)
    {
        _pipeline = pipeline;
        _journeyContext = journeyContext;
        _journeyEmitter = journeyEmitter;
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        PopulateJourneyContext(body);
        await _journeyEmitter.EmitAsync(
            "router",
            "request_received",
            fromComponent: "gateway",
            toComponent: "cwi-router",
            metadata: new Dictionary<string, object?>
            {
                ["question_length"] = TryGetQuestionLength(body)
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var result = await ProxyAsync(body, cancellationToken).ConfigureAwait(false);
        await _journeyEmitter.EmitAsync(
            "router",
            "response_returned",
            direction: "response",
            fromComponent: "cwi-router",
            toComponent: "gateway",
            statusCode: HttpContext.Response.StatusCode,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result;
    }

    [HttpGet("ask/deferred/{workId}")]
    public Task<IActionResult> AskDeferred(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("action")]
    public Task<IActionResult> Action(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("approval/complete")]
    public Task<IActionResult> ApprovalComplete(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("break-glass/issue")]
    public Task<IActionResult> BreakGlassIssue(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("break-glass/revoke")]
    public Task<IActionResult> BreakGlassRevoke(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("break-glass/protected")]
    public Task<IActionResult> BreakGlassProtected(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("break-glass/audit")]
    public Task<IActionResult> BreakGlassAudit(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("ingest/telemetry")]
    public Task<IActionResult> TelemetryIngest(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("ingest/document")]
    public Task<IActionResult> DocumentIngest(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("feedback")]
    public Task<IActionResult> Feedback(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("feedback/report")]
    public Task<IActionResult> FeedbackReport(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("survival/signal")]
    public Task<IActionResult> SurvivalSignal(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("survival/status")]
    public Task<IActionResult> SurvivalStatus(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("survival/offline-response")]
    public Task<IActionResult> SurvivalOfflineResponse(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("feedback/summary")]
    public Task<IActionResult> FeedbackSummary(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("ledger/export")]
    public Task<IActionResult> LedgerExport(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("reconciliation/records")]
    public Task<IActionResult> ReconciliationUpsert(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("reconciliation/records/{recordId}")]
    public Task<IActionResult> ReconciliationGet(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("reconciliation/records/{recordId}/report")]
    public Task<IActionResult> ReconciliationReport(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("reconciliation/records/{recordId}/approval/complete")]
    public Task<IActionResult> ReconciliationApprovalComplete(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("reports")]
    public Task<IActionResult> ReportsList(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("reports/{reportId}")]
    public Task<IActionResult> ReportsGet(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("history")]
    public Task<IActionResult> HistoryList(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("history")]
    public Task<IActionResult> HistoryRecord(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpDelete("history")]
    public Task<IActionResult> HistoryClear(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("account/home-node")]
    public Task<IActionResult> AccountHomeNodeGet(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("account/home-node")]
    public Task<IActionResult> AccountHomeNodeLink(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpDelete("account/home-node")]
    public Task<IActionResult> AccountHomeNodeUnlink(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("account/connectors")]
    public Task<IActionResult> AccountConnectorsList(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("account/connectors")]
    public Task<IActionResult> AccountConnectorsCreate(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPatch("account/connectors/{connectionId}")]
    public Task<IActionResult> AccountConnectorsUpdate(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpDelete("account/connectors/{connectionId}")]
    public Task<IActionResult> AccountConnectorsDisconnect(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("account/connectors/{connectionId}/sync")]
    public Task<IActionResult> AccountConnectorsQueueSync(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("documents")]
    public Task<IActionResult> DocumentsList(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("documents/{docId}")]
    public Task<IActionResult> DocumentsGet(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("documents/{docId}/rollback")]
    public Task<IActionResult> DocumentsRollback(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("ownership/lineage")]
    public Task<IActionResult> OwnershipLineage(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("ownership/revocations")]
    public Task<IActionResult> OwnershipRevocations(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("ownership/delegate")]
    public Task<IActionResult> OwnershipDelegate(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("ownership/revoke")]
    public Task<IActionResult> OwnershipRevoke(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("mesh/links")]
    public Task<IActionResult> MeshLinks(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("mesh/events")]
    public Task<IActionResult> MeshEvents(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("mesh/hints")]
    public Task<IActionResult> MeshHints(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("trace/{traceId}")]
    public Task<IActionResult> TraceLookup(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("dev/fault")]
    public Task<IActionResult> DevFault(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("admin/kill_switch")]
    public Task<IActionResult> KillSwitchStatus(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("admin/kill_switch")]
    public Task<IActionResult> KillSwitchUpdate(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpPost("admin/kill_switch/token")]
    public Task<IActionResult> KillSwitchToken(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("journey/requests/{requestId}")]
    public Task<IActionResult> JourneyByRequest(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    [HttpGet("journey/requests/{requestId}/stream")]
    public Task<IActionResult> JourneyByRequestStream(CancellationToken cancellationToken)
    {
        return ProxyAsync(cancellationToken);
    }

    private async Task<IActionResult> ProxyAsync(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return await ProxyAsync(body, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IActionResult> ProxyAsync(string body, CancellationToken cancellationToken)
    {
        if (Request.Path.Value?.Equals("/api/ask", StringComparison.OrdinalIgnoreCase) == true && string.IsNullOrWhiteSpace(_journeyContext.RequestId))
        {
            PopulateJourneyContext(body);
        }

        var context = new RouterRequestContext(HttpContext, body);
        var result = await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            var error = new ErrorResponse
            {
                Code = result.Error?.Code ?? "internal_error",
                Error = result.Error?.Code ?? "internal_error",
                Message = result.Error?.Message ?? result.Message,
                TraceId = HttpContext.TraceIdentifier
            };

            return StatusCode(result.StatusCode, error);
        }

        if (result.Data!.IsStream && result.Data.BodyStream is not null)
        {
            Response.StatusCode = result.Data.StatusCode;
            Response.ContentType = result.Data.ContentType;
            await result.Data.BodyStream.CopyToAsync(Response.Body, cancellationToken).ConfigureAwait(false);
            await result.Data.BodyStream.DisposeAsync().ConfigureAwait(false);
            return new EmptyResult();
        }

        return new ContentResult
        {
            StatusCode = result.Data!.StatusCode,
            ContentType = result.Data.ContentType,
            Content = result.Data.Body
        };
    }

    private void PopulateJourneyContext(string body)
    {
        var requestId = Request.Headers["x-request-id"].ToString();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            requestId = HttpContext.TraceIdentifier;
        }

        _journeyContext.RequestId = requestId;
        _journeyContext.TraceId = HttpContext.TraceIdentifier;
        _journeyContext.Method = Request.Method;
        _journeyContext.Path = Request.Path.Value;

        string? tenantId = null;
        string? userId = null;

        if (string.IsNullOrWhiteSpace(body))
        {
            tenantId = ResolveIdentityValue("tenant_id", "x-tenant-id");
            userId = ResolveIdentityValue("sub", "x-user-id")
                ?? ResolveIdentityValue("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier", "x-user-id");
            _journeyContext.TenantId = tenantId;
            _journeyContext.UserId = userId;
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("tenant_id", out var tenant) && tenant.ValueKind == JsonValueKind.String)
            {
                tenantId = tenant.GetString();
            }
            if (root.TryGetProperty("user_id", out var user) && user.ValueKind == JsonValueKind.String)
            {
                userId = user.GetString();
            }
            if (root.TryGetProperty("pack", out var pack) && pack.ValueKind == JsonValueKind.Object)
            {
                if (pack.TryGetProperty("name", out var packName) && packName.ValueKind == JsonValueKind.String)
                {
                    _journeyContext.PackName = packName.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }

        _journeyContext.TenantId = ResolveIdentityValue("tenant_id", "x-tenant-id", tenantId);
        _journeyContext.UserId = ResolveIdentityValue(
            "sub",
            "x-user-id",
            userId,
            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");
    }

    private string? ResolveIdentityValue(
        string primaryTokenClaim,
        string headerName,
        string? fallback = null,
        string? secondaryTokenClaim = null)
    {
        var tokenValue = TryReadTokenClaim(primaryTokenClaim);
        if (!string.IsNullOrWhiteSpace(tokenValue))
        {
            return tokenValue;
        }

        if (!string.IsNullOrWhiteSpace(secondaryTokenClaim))
        {
            tokenValue = TryReadTokenClaim(secondaryTokenClaim);
            if (!string.IsNullOrWhiteSpace(tokenValue))
            {
                return tokenValue;
            }
        }

        var headerValue = Request.Headers[headerName].ToString();
        if (!string.IsNullOrWhiteSpace(headerValue))
        {
            return headerValue;
        }

        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }

    private string? TryReadTokenClaim(string claimName)
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader) ||
            !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var json = DecodeBase64Url(parts[1]);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty(claimName, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        var padding = 4 - (base64.Length % 4);
        if (padding is > 0 and < 4)
        {
            base64 = base64.PadRight(base64.Length + padding, '=');
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    private static int TryGetQuestionLength(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("question", out var question) && question.ValueKind == JsonValueKind.String
                ? question.GetString()?.Length ?? 0
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
