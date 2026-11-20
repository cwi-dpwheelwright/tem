using Cwi.Common;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.History;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Api.Health;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Common.Errors;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("mcp/ask")]
[Route("api/ask")]
public sealed class AskController : ControllerBase
{
    private static bool IsAskReady(DependencyHealthReport report)
    {
        if (report.IsHealthy)
        {
            return true;
        }

        return !report.Dependencies.Any(dep =>
            !dep.IsHealthy &&
            !dep.Name.StartsWith("vector_shard", StringComparison.OrdinalIgnoreCase));
    }

    private readonly AskService _askService;
    private readonly DependencyHealthChecker _healthChecker;
    private readonly IAskHistoryService _historyService;
    private readonly JourneyRequestContext _journeyContext;
    private readonly JourneyEmitter _journeyEmitter;

    public AskController(
        AskService askService,
        DependencyHealthChecker healthChecker,
        IAskHistoryService historyService,
        JourneyRequestContext journeyContext,
        JourneyEmitter journeyEmitter)
    {
        _askService = askService;
        _healthChecker = healthChecker;
        _historyService = historyService;
        _journeyContext = journeyContext;
        _journeyEmitter = journeyEmitter;
    }

    [HttpPost]
    public async Task<ActionResult<AskResponseDto>> Ask([FromBody] AskRequestDto request, CancellationToken cancellationToken)
    {
        var requestId = HttpContext.Request.Headers["x-request-id"].ToString();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            requestId = Guid.NewGuid().ToString("n");
        }

        var tenantId = User.FindFirst("tenant_id")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            tenantId = request.TenantId?.Trim();
        }

        var userId = User.FindFirst("sub")?.Value?.Trim()
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(userId))
        {
            userId = request.UserId?.Trim();
        }

        _journeyContext.RequestId = requestId;
        _journeyContext.TraceId = HttpContext.TraceIdentifier;
        _journeyContext.TenantId = tenantId;
        _journeyContext.UserId = userId;
        _journeyContext.PackName = request.Pack.Name;
        _journeyContext.Method = HttpContext.Request.Method;
        _journeyContext.Path = HttpContext.Request.Path.Value;

        await _journeyEmitter.EmitAsync(
            "orchestrator",
            "request_received",
            fromComponent: "router",
            toComponent: "cwi-orchestrator",
            metadata: new Dictionary<string, object?>
            {
                ["question_length"] = request.Question?.Length ?? 0
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            await _journeyEmitter.EmitAsync(
                "orchestrator",
                "response_failed",
                direction: "response",
                fromComponent: "cwi-orchestrator",
                toComponent: "router",
                statusCode: 400,
                message: "Question is required.",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return BadRequest(new DomainError(ErrorCodes.InvalidRequest, "Question is required."));
        }

        var historyQuestion = AskQuestionScope.ExtractPrimaryQuestion(request.Question).Trim();
        if (string.IsNullOrWhiteSpace(historyQuestion))
        {
            historyQuestion = request.Question.Trim();
        }

        await TryRecordHistoryAsync(
            tenantId ?? string.Empty,
            userId ?? string.Empty,
            request,
            requestId,
            historyQuestion,
            answer: string.Empty,
            responseStatus: "processing",
            trace: null,
            cancellationToken).ConfigureAwait(false);

        var report = await _healthChecker.CheckAsync(cancellationToken).ConfigureAwait(false);
        if (!IsAskReady(report))
        {
            await TryRecordHistoryAsync(
                tenantId ?? string.Empty,
                userId ?? string.Empty,
                request,
                requestId,
                historyQuestion,
                "Request failed before a response was produced.",
                "failed",
                trace: null,
                cancellationToken).ConfigureAwait(false);
            await _journeyEmitter.EmitAsync(
                "orchestrator",
                "response_failed",
                direction: "response",
                fromComponent: "cwi-orchestrator",
                toComponent: "router",
                statusCode: 503,
                message: "Dependencies unavailable.",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return StatusCode(503, new DomainError(ErrorCodes.DependencyUnavailable, "Dependencies unavailable."));
        }

        var modelTier = HttpContext.Request.Headers["x-model-tier"].ToString();
        ServiceResponse<AskResponseDto> result;
        try
        {
            result = await _askService.HandleAsync(
                tenantId ?? string.Empty,
                userId ?? string.Empty,
                request.Question,
                request.Pack.Name,
                request.Pack.Anchor,
                request.Context.Mode,
                request.Context.TopK,
                modelTier,
                request.Context.ConversationId,
                request.Context.Timezone,
                cancellationToken,
                webRetrieval: request.Context.WebRetrieval,
                useWebRequested: request.Context.UseWeb == true || request.Context.TrustedWebRequested == true).ConfigureAwait(false);
        }
        catch
        {
            await TryRecordHistoryAsync(
                tenantId ?? string.Empty,
                userId ?? string.Empty,
                request,
                requestId,
                historyQuestion,
                "Request failed before a response was produced.",
                "failed",
                trace: null,
                cancellationToken).ConfigureAwait(false);
            throw;
        }

        if (!result.IsSuccess || result.Data is null)
        {
            await TryRecordHistoryAsync(
                tenantId ?? string.Empty,
                userId ?? string.Empty,
                request,
                requestId,
                historyQuestion,
                result.Error?.Message ?? result.Message ?? "Request failed before a response was produced.",
                "failed",
                trace: null,
                cancellationToken).ConfigureAwait(false);
            await _journeyEmitter.EmitAsync(
                "orchestrator",
                "response_failed",
                direction: "response",
                fromComponent: "cwi-orchestrator",
                toComponent: "router",
                statusCode: result.StatusCode,
                message: result.Error?.Message ?? result.Message,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return StatusCode(result.StatusCode, result.Error);
        }

        _journeyContext.TraceId = result.Data.Trace.TraceId;
        await _journeyEmitter.EmitAsync(
            "orchestrator",
            "response_ready",
            direction: "response",
            fromComponent: "cwi-orchestrator",
            toComponent: "router",
            statusCode: 200,
            metadata: new Dictionary<string, object?>
            {
                ["graph_hits"] = result.Data.Trace.GraphHits,
                ["document_hits"] = result.Data.Trace.DocumentHits,
                ["confidence"] = result.Data.Trace.Confidence
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var deferredHomeNodeWork = IsDeferredHomeNodeWork(result.Data);
        await TryRecordHistoryAsync(
            tenantId ?? string.Empty,
            userId ?? string.Empty,
            request,
            requestId,
            historyQuestion,
            deferredHomeNodeWork ? string.Empty : result.Data.Answer,
            deferredHomeNodeWork ? "processing" : "completed",
            result.Data.Trace,
            cancellationToken).ConfigureAwait(false);

        return Ok(result.Data);
    }

    private static bool IsDeferredHomeNodeWork(AskResponseDto response)
    {
        return string.Equals(response.ExecutionState, "pending", StringComparison.OrdinalIgnoreCase)
            && string.Equals(response.ExecutionTarget, "home_node", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(response.DeferredWorkId);
    }

    private async Task TryRecordHistoryAsync(
        string tenantId,
        string userId,
        AskRequestDto request,
        string requestId,
        string question,
        string answer,
        string responseStatus,
        TracePayload? trace,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        try
        {
            await _historyService.RecordAsync(
                tenantId,
                userId,
                new AskHistoryRecordRequestDto
                {
                    RequestId = requestId,
                    ConversationId = request.Context.ConversationId,
                    Question = question,
                    Answer = answer,
                    ResponseStatus = responseStatus,
                    TraceId = trace?.TraceId,
                    PackName = request.Pack.Name,
                    PackHash = request.Pack.Anchor,
                    Trace = SerializeTrace(trace)
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _journeyEmitter.EmitAsync(
                "orchestrator",
                "history_sync_failed",
                fromComponent: "cwi-orchestrator",
                toComponent: "history-store",
                message: "Ask history lifecycle update failed.",
                metadata: new Dictionary<string, object?>
                {
                    ["request_id"] = requestId,
                    ["response_status"] = responseStatus
                },
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static System.Text.Json.JsonElement? SerializeTrace(TracePayload? trace)
    {
        if (trace is null)
        {
            return null;
        }

        return System.Text.Json.JsonSerializer.SerializeToElement(trace);
    }
}
