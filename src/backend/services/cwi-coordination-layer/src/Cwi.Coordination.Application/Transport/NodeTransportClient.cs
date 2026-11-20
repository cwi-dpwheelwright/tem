using System.Net.Http.Json;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Cwi.Coordination.Application.Transport;

public sealed class NodeTransportClient
{
    private readonly HttpClient _httpClient;
    private readonly ResiliencePipeline _pipeline;
    private readonly SemaphoreSlim _rateLimiter = new(20, 20);

    public NodeTransportClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _pipeline = new ResiliencePipelineBuilder()
            .AddTimeout(TimeSpan.FromSeconds(5))
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 10,
                BreakDuration = TimeSpan.FromSeconds(15)
            })
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true
            })
            .Build();
    }

    public Task<HttpResponseMessage> PostAsync<T>(string url, T payload, CancellationToken cancellationToken)
    {
        return ExecuteRateLimitedAsync(
            () => _pipeline.ExecuteAsync(
                async token => await _httpClient.PostAsJsonAsync(url, payload, token).ConfigureAwait(false),
                cancellationToken),
            cancellationToken);
    }

    private async Task<HttpResponseMessage> ExecuteRateLimitedAsync(
        Func<ValueTask<HttpResponseMessage>> action,
        CancellationToken cancellationToken)
    {
        await _rateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            _rateLimiter.Release();
        }
    }
}
