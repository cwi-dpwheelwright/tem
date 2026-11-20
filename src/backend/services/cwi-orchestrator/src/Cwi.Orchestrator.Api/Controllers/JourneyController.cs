using System.Text.Json;
using Cwi.Common.Journey;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Api.Journey;
using Cwi.Orchestrator.Application.Journey;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/journey")]
public sealed class JourneyController : ControllerBase
{
    private readonly IJourneyService _journeyService;
    private readonly JourneyBroadcaster _broadcaster;

    public JourneyController(IJourneyService journeyService, JourneyBroadcaster broadcaster)
    {
        _journeyService = journeyService;
        _broadcaster = broadcaster;
    }

    [HttpGet("requests/{requestId}")]
    public async Task<IActionResult> ListByRequestId(string requestId, CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var tenantId, out var userId))
        {
            return Unauthorized();
        }

        var items = await _journeyService.ListByRequestIdAsync(requestId, cancellationToken).ConfigureAwait(false);
        items = items.Where(item => CanView(item, tenantId, userId)).ToList();
        return Ok(new { request_id = requestId, items });
    }

    [HttpGet("requests/{requestId}/stream")]
    public async Task StreamByRequestId(string requestId, CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var tenantId, out var userId))
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        var initial = await _journeyService.ListByRequestIdAsync(requestId, cancellationToken).ConfigureAwait(false);
        foreach (var item in initial)
        {
            if (!CanView(item, tenantId, userId))
            {
                continue;
            }
            await WriteEventAsync("journey", item, cancellationToken).ConfigureAwait(false);
        }

        await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

        await using var subscription = _broadcaster.Subscribe(requestId);
        while (!cancellationToken.IsCancellationRequested)
        {
            var readTask = subscription.Reader.ReadAsync(cancellationToken).AsTask();
            var heartbeatTask = Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            var completed = await Task.WhenAny(readTask, heartbeatTask).ConfigureAwait(false);

            if (completed == heartbeatTask)
            {
                await Response.WriteAsync(": keepalive\n\n", cancellationToken).ConfigureAwait(false);
                await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            var item = await readTask.ConfigureAwait(false);
            if (!CanView(item, tenantId, userId))
            {
                continue;
            }
            await WriteEventAsync("journey", item, cancellationToken).ConfigureAwait(false);
            await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WriteEventAsync(string eventName, JourneyEvent item, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(item);
        await Response.WriteAsync($"event: {eventName}\n", cancellationToken).ConfigureAwait(false);
        await Response.WriteAsync($"data: {payload}\n\n", cancellationToken).ConfigureAwait(false);
    }

    private static bool CanView(JourneyEvent item, string tenantId, string userId)
    {
        return string.Equals(item.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.UserId, userId, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetIdentity(out string tenantId, out string userId)
    {
        tenantId = User.FindFirst("tenant_id")?.Value?.Trim() ?? string.Empty;
        userId = User.FindFirst("sub")?.Value?.Trim()
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim()
            ?? string.Empty;

        return !string.IsNullOrWhiteSpace(tenantId) && !string.IsNullOrWhiteSpace(userId);
    }
}
