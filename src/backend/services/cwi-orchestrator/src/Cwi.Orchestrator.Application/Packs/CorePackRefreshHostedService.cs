using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cwi.Orchestrator.Application.Packs;

public sealed class CorePackRefreshHostedService : BackgroundService
{
    private readonly ICorePackRuntimeCatalog _catalog;
    private readonly ApplicationOptions _options;
    private readonly ILogger<CorePackRefreshHostedService> _logger;

    public CorePackRefreshHostedService(
        ICorePackRuntimeCatalog catalog,
        ApplicationOptions options,
        ILogger<CorePackRefreshHostedService> logger)
    {
        _catalog = catalog;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_catalog.Enabled)
        {
            _logger.LogInformation("Core pack refresh hosted service is disabled.");
            return;
        }

        await _catalog.RefreshAllAsync("startup", stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.CorePackRefreshIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested
               && await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await _catalog.RefreshAllAsync("interval", stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Core pack refresh interval failed.");
            }
        }
    }
}
