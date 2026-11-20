using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;

namespace Cwi.Orchestrator.Application.Http;

public sealed class ResilienceHandler : DelegatingHandler
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan CircuitOpenDuration = TimeSpan.FromSeconds(30);
    private const int FailureThreshold = 3;
    private static readonly ConcurrentDictionary<string, CircuitState> Circuits = new();
    private static readonly HttpStatusCode[] RetryStatusCodes =
    {
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    };

    private readonly ILogger<ResilienceHandler> _logger;

    public ResilienceHandler(ILogger<ResilienceHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A transport failure does not establish that a write or inference was not
        // executed. Recovery belongs to the caller's durable protocol, not HTTP.
        var maxAttempts = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head
            ? MaxAttempts
            : 1;
        var circuitKey = request.RequestUri?.Host ?? "unknown";
        if (IsCircuitOpen(circuitKey))
        {
            _logger.LogWarning("Circuit open for {CircuitKey}", circuitKey);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                ReasonPhrase = "Circuit open"
            };
        }

        var contentBytes = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var attemptRequest = CloneRequest(request, contentBytes);
            try
            {
                var response = await base.SendAsync(attemptRequest, cancellationToken).ConfigureAwait(false);
                if (!ShouldRetry(response.StatusCode) || attempt == maxAttempts)
                {
                    if ((int)response.StatusCode < 500)
                    {
                        ResetCircuit(circuitKey);
                    }
                    else
                    {
                        RecordFailure(circuitKey);
                    }
                    return response;
                }

                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                RecordFailure(circuitKey);
            }

            await Task.Delay(ComputeDelay(attempt), cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("HTTP attempt budget exhausted.");
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request, byte[]? contentBytes)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in request.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        if (contentBytes is not null)
        {
            clone.Content = new ByteArrayContent(contentBytes);
            foreach (var header in request.Content!.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        if ((int)statusCode >= 500)
        {
            return true;
        }

        return RetryStatusCodes.Contains(statusCode);
    }

    private static TimeSpan ComputeDelay(int attempt)
    {
        var factor = Math.Pow(2, attempt - 1);
        return TimeSpan.FromMilliseconds(BaseDelay.TotalMilliseconds * factor);
    }

    private static bool IsCircuitOpen(string key)
    {
        if (!Circuits.TryGetValue(key, out var state))
        {
            return false;
        }

        if (state.OpenUntilUtc is null)
        {
            return false;
        }

        if (DateTimeOffset.UtcNow >= state.OpenUntilUtc)
        {
            Circuits[key] = state with { Failures = 0, OpenUntilUtc = null };
            return false;
        }

        return true;
    }

    private static void RecordFailure(string key)
    {
        var state = Circuits.GetOrAdd(key, _ => new CircuitState());
        var failures = state.Failures + 1;
        if (failures >= FailureThreshold)
        {
            Circuits[key] = state with { Failures = failures, OpenUntilUtc = DateTimeOffset.UtcNow.Add(CircuitOpenDuration) };
            return;
        }

        Circuits[key] = state with { Failures = failures };
    }

    private static void ResetCircuit(string key)
    {
        if (Circuits.TryGetValue(key, out var state) && (state.Failures > 0 || state.OpenUntilUtc is not null))
        {
            Circuits[key] = state with { Failures = 0, OpenUntilUtc = null };
        }
    }

    private sealed record CircuitState(int Failures = 0, DateTimeOffset? OpenUntilUtc = null);
}
