using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cwi.Router.Application.Survival;

public sealed class SurvivalPoller : BackgroundService
{
    private readonly SurvivalStatusClient _client;
    private readonly SurvivalStateStore _store;
    private readonly SurvivalOptions _options;
    private readonly ILogger<SurvivalPoller> _logger;

    public SurvivalPoller(
        SurvivalStatusClient client,
        SurvivalStateStore store,
        SurvivalOptions options,
        ILogger<SurvivalPoller> logger)
    {
        _client = client;
        _store = store;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var delay = TimeSpan.FromSeconds(Math.Max(5, _options.PollSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var status = await _client.GetStatusAsync(stoppingToken).ConfigureAwait(false);
                if (status is not null)
                {
                    _store.Update(status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "survival poller failed");
            }

            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}
