using Cwi.Router.Application.Pipeline;
using Microsoft.AspNetCore.Http;

namespace Cwi.Router.Application.Clients;

public sealed class CoordinationClientRetryDecorator : ICoordinationClient
{
    private readonly ICoordinationClient _inner;
    private readonly int _maxAttempts;
    private readonly TimeSpan _delay;

    public CoordinationClientRetryDecorator(ICoordinationClient inner, int maxAttempts, TimeSpan delay)
    {
        _inner = inner;
        _maxAttempts = Math.Max(1, maxAttempts);
        _delay = delay;
    }

    public async Task<RouterResult> ForwardAsync(RouterRequestContext context, CancellationToken cancellationToken)
    {
        var request = context.HttpContext.Request;
        var method = request.Method?.ToUpperInvariant() ?? "GET";
        var isIdempotent = method is "GET" or "HEAD" or "OPTIONS" || HasIdempotencyKey(request, context);
        var attempts = isIdempotent ? _maxAttempts : 1;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                var result = await _inner.ForwardAsync(context, cancellationToken).ConfigureAwait(false);
                if (result.StatusCode >= 500 && attempt < attempts)
                {
                    await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return result;
            }
            catch (HttpRequestException) when (attempt < attempts)
            {
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new HttpRequestException("Coordination request failed after retries.");
    }

    private static bool HasIdempotencyKey(HttpRequest request, RouterRequestContext context)
    {
        if (!string.IsNullOrWhiteSpace(request.Headers["Idempotency-Key"]))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(request.Headers["X-Idempotency-Key"]))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(context.HttpContext.TraceIdentifier))
        {
            request.Headers["X-Idempotency-Key"] = context.HttpContext.TraceIdentifier;
            return true;
        }

        return false;
    }
}
