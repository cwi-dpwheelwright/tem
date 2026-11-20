using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.Journey;
using Cwi.Router.Application.Pipeline;
using Microsoft.AspNetCore.Http;

namespace Cwi.Router.Application.Clients;

public sealed class OrchestratorClient : IOrchestratorClient
{
    private readonly HttpClient _httpClient;
    private readonly RouterConfig _config;
    private readonly IReadOnlyList<string> _endpoints;
    private readonly RouterJourneyEmitter _journeyEmitter;

    public OrchestratorClient(
        IHttpClientFactory httpClientFactory,
        RouterConfig config,
        RouterJourneyEmitter journeyEmitter)
    {
        _httpClient = httpClientFactory.CreateClient("orchestrator");
        _config = config;
        _endpoints = config.OrchestratorUrls;
        _journeyEmitter = journeyEmitter;
    }

    public async Task<RouterResult> ForwardAsync(RouterRequestContext context, CancellationToken cancellationToken)
    {
        var request = context.HttpContext.Request;
        var endpoints = GetOrderedEndpoints(context);
        Exception? lastException = null;

        foreach (var endpoint in endpoints)
        {
            var target = new Uri(new Uri(endpoint), request.Path + request.QueryString);
            using var message = new HttpRequestMessage(new HttpMethod(request.Method), target);

            if (!string.IsNullOrWhiteSpace(context.Body))
            {
                var mediaType = NormalizeContentType(request.ContentType);
                message.Content = new StringContent(
                    context.Body,
                    System.Text.Encoding.UTF8,
                    mediaType);
            }

            foreach (var header in request.Headers)
            {
                if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                {
                    message.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
            }

            EnsureIdempotencyKey(message, request, context);

            try
            {
                var started = DateTimeOffset.UtcNow;
                await _journeyEmitter.EmitAsync(
                    "router_forwarder",
                    "forwarded",
                    fromComponent: "cwi-router",
                    toComponent: "cwi-orchestrator",
                    metadata: new Dictionary<string, object?> { ["target"] = target.ToString() },
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                var response = await _httpClient
                    .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
                var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
                await _journeyEmitter.EmitAsync(
                    "router_forwarder",
                    "response_received",
                    direction: "response",
                    fromComponent: "cwi-orchestrator",
                    toComponent: "cwi-router",
                    statusCode: (int)response.StatusCode,
                    durationMs: (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
                    metadata: new Dictionary<string, object?> { ["target"] = target.ToString() },
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (contentType.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase))
                {
                    var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    return new RouterResult((int)response.StatusCode, stream, contentType);
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                response.Dispose();

                if ((int)response.StatusCode >= 500 && endpoints.Count > 1)
                {
                    lastException = new HttpRequestException($"Upstream {endpoint} returned {(int)response.StatusCode}");
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return new RouterResult((int)response.StatusCode, body, contentType);
            }
            catch (Exception ex) when (endpoints.Count > 1)
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new HttpRequestException("Orchestrator request failed.", lastException);
    }

    private IReadOnlyList<string> GetOrderedEndpoints(RouterRequestContext context)
    {
        if (_endpoints.Count <= 1)
        {
            return _endpoints;
        }

        var seed = context.HttpContext.TraceIdentifier ?? context.TenantId ?? Guid.NewGuid().ToString("N");
        var index = (int)(ComputeFnv1a32(seed) % (uint)_endpoints.Count);
        var ordered = new List<string>(_endpoints.Count);
        for (var i = 0; i < _endpoints.Count; i++)
        {
            ordered.Add(_endpoints[(index + i) % _endpoints.Count]);
        }
        return ordered;
    }

    private static uint ComputeFnv1a32(string value)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= prime;
        }

        return hash;
    }

    private static void EnsureIdempotencyKey(
        HttpRequestMessage message,
        HttpRequest request,
        RouterRequestContext context)
    {
        if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var existing = request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(existing))
        {
            existing = request.Headers["X-Idempotency-Key"].ToString();
        }

        if (string.IsNullOrWhiteSpace(existing))
        {
            existing = context.HttpContext.TraceIdentifier;
        }

        if (!string.IsNullOrWhiteSpace(existing))
        {
            message.Headers.TryAddWithoutValidation("Idempotency-Key", existing);
        }
    }

    private static string NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return "application/json";
        }

        var index = contentType.IndexOf(';');
        if (index > 0)
        {
            contentType = contentType[..index];
        }

        return string.IsNullOrWhiteSpace(contentType) ? "application/json" : contentType.Trim();
    }
}
