using Cwi.Router.Application.Configuration;
using StackExchange.Redis;

namespace Cwi.Router.Application.Health;

public sealed class DependencyHealthChecker
{
    private readonly RouterConfig _config;
    private readonly IHttpClientFactory _httpClientFactory;

    public DependencyHealthChecker(RouterConfig config, IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<DependencyHealthReport> CheckAsync(CancellationToken cancellationToken)
    {
        var orchestrator = await CheckOrchestratorAsync(cancellationToken).ConfigureAwait(false);
        var redis = await CheckRedisAsync(cancellationToken).ConfigureAwait(false);

        return new DependencyHealthReport(orchestrator, redis);
    }

    private async Task<DependencyStatus> CheckOrchestratorAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            var client = _httpClientFactory.CreateClient("orchestrator");
            var response = await client.GetAsync("/api/health", timeoutCts.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? DependencyStatus.Ok("orchestrator")
                : DependencyStatus.Failed("orchestrator", $"HTTP {response.StatusCode}");
        }
        catch (OperationCanceledException)
        {
            return DependencyStatus.Failed("orchestrator", "Timeout");
        }
        catch (Exception ex)
        {
            return DependencyStatus.Failed("orchestrator", ex.GetType().Name);
        }
    }

    private async Task<DependencyStatus> CheckRedisAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.RedisUrl))
        {
            return DependencyStatus.Failed("redis", "REDIS_URL not configured");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

        Exception? lastException = null;
        for (var attempt = 0; attempt < 3; attempt += 1)
        {
            if (timeoutCts.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var options = ConfigurationOptions.Parse(_config.RedisUrl);
                options.AbortOnConnectFail = false;
                options.ConnectTimeout = 3000;
                options.ConnectRetry = 2;
                options.AsyncTimeout = 3000;

                var connectTask = ConnectionMultiplexer.ConnectAsync(options);
                var connectCompleted = await Task.WhenAny(
                        connectTask,
                        Task.Delay(TimeSpan.FromSeconds(3), timeoutCts.Token))
                    .ConfigureAwait(false);
                if (connectCompleted != connectTask)
                {
                    lastException = new TimeoutException("Timeout");
                    continue;
                }

                using var connection = await connectTask.ConfigureAwait(false);
                var db = connection.GetDatabase();
                var pingTask = db.PingAsync();
                var pingCompleted = await Task.WhenAny(
                        pingTask,
                        Task.Delay(TimeSpan.FromSeconds(3), timeoutCts.Token))
                    .ConfigureAwait(false);
                if (pingCompleted != pingTask)
                {
                    lastException = new TimeoutException("Timeout");
                    continue;
                }

                await pingTask.ConfigureAwait(false);
                return DependencyStatus.Ok("redis");
            }
            catch (OperationCanceledException)
            {
                lastException = new TimeoutException("Timeout");
                break;
            }
            catch (Exception ex)
            {
                lastException = ex;
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        return DependencyStatus.Failed("redis", lastException?.GetType().Name ?? "Timeout");
    }
}

