using Cwi.Common.Events;
using Cwi.Router.Application.EventBus;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Router.Api.Controllers;

[ApiController]
[Route("api/events")]
public sealed class EventsController : ControllerBase
{
    private readonly EventBusOptions _options;
    private readonly EventBusPublisher _publisher;
    private readonly EventBusReader _reader;

    public EventsController(EventBusOptions options, EventBusPublisher publisher, EventBusReader reader)
    {
        _options = options;
        _publisher = publisher;
        _reader = reader;
    }

    [HttpPost]
    public async Task<IActionResult> Publish([FromBody] EventEnvelope payload, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (!AuthorizeRequest(_options.WriteToken))
        {
            return Unauthorized();
        }

        var envelope = payload with
        {
            EventId = string.IsNullOrWhiteSpace(payload.EventId) ? Guid.NewGuid().ToString("n") : payload.EventId,
            TimestampUtc = payload.TimestampUtc == default ? DateTimeOffset.UtcNow : payload.TimestampUtc,
            Type = string.IsNullOrWhiteSpace(payload.Type) ? "event" : payload.Type,
            Polarity = string.IsNullOrWhiteSpace(payload.Polarity) ? "positive" : payload.Polarity,
            Severity = string.IsNullOrWhiteSpace(payload.Severity) ? "info" : payload.Severity
        };

        await _publisher.PublishAsync(envelope, cancellationToken).ConfigureAwait(false);
        return Accepted(new { status = "queued" });
    }

    [HttpGet]
    public async Task<ActionResult<EventPage>> Read(
        [FromQuery(Name = "before_id")] string? beforeId,
        [FromQuery(Name = "limit")] int limit = 100,
        [FromQuery(Name = "service")] string? service = null,
        [FromQuery(Name = "polarity")] string? polarity = null,
        [FromQuery(Name = "type")] string? type = null,
        [FromQuery(Name = "severity")] string? severity = null,
        [FromQuery(Name = "tenant_id")] string? tenantId = null)
    {
        if (!_options.Enabled)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (!AuthorizeRequest(_options.ReadToken))
        {
            return Unauthorized();
        }

        var safeLimit = Math.Clamp(limit, 1, 1000);
        var items = await _reader.ReadLatestAsync(beforeId, safeLimit * 2).ConfigureAwait(false);

        var filtered = items.Where(item =>
        {
            var ev = item.Event;
            if (!string.IsNullOrWhiteSpace(service) &&
                !string.Equals(ev.Service, service, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(polarity) &&
                !string.Equals(ev.Polarity, polarity, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(type) &&
                !string.Equals(ev.Type, type, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(severity) &&
                !string.Equals(ev.Severity, severity, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(tenantId) &&
                !string.Equals(ev.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return true;
        }).Take(safeLimit).ToList();

        var nextBefore = filtered.Count > 0 ? filtered[^1].StreamId : null;
        return Ok(new EventPage(filtered, nextBefore));
    }

    private bool AuthorizeRequest(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var header = authHeader.ToString();
            if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var value = header.Substring("Bearer ".Length).Trim();
                return string.Equals(value, token, StringComparison.Ordinal);
            }
        }

        if (Request.Headers.TryGetValue("X-Events-Token", out var tokenHeader))
        {
            return string.Equals(tokenHeader.ToString(), token, StringComparison.Ordinal);
        }

        return false;
    }
}

public sealed record EventPage(IReadOnlyList<EventStreamItem> Items, string? NextBeforeId);
