using System.Diagnostics;
using Microsoft.Extensions.Hosting;

namespace Cwi.Router.Api.Trace;

public sealed class TraceSpanListener : IHostedService, IDisposable
{
    private readonly TraceSpanStore _store;
    private readonly TraceQueryOptions _options;
    private ActivityListener? _listener;

    public TraceSpanListener(TraceSpanStore store, TraceQueryOptions options)
    {
        _store = store;
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => _store.Record(activity)
        };
        ActivitySource.AddActivityListener(_listener);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _listener?.Dispose();
        _listener = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _listener?.Dispose();
        _listener = null;
    }
}
