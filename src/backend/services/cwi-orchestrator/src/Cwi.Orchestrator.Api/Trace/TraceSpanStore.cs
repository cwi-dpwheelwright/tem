using System.Collections.Concurrent;
using System.Diagnostics;

namespace Cwi.Orchestrator.Api.Trace;

public sealed class TraceSpanStore
{
    private readonly TraceQueryOptions _options;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<TraceSpanRecord>> _spansByTrace = new();
    private readonly ConcurrentQueue<string> _traceOrder = new();

    public TraceSpanStore(TraceQueryOptions options)
    {
        _options = options;
    }

    public void Record(Activity activity)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var traceId = activity.TraceId.ToString();
        if (string.IsNullOrWhiteSpace(traceId))
        {
            return;
        }

        var queue = _spansByTrace.GetOrAdd(traceId, id =>
        {
            _traceOrder.Enqueue(id);
            TrimTraceList();
            return new ConcurrentQueue<TraceSpanRecord>();
        });

        queue.Enqueue(new TraceSpanRecord
        {
            TraceId = traceId,
            SpanId = activity.SpanId.ToString(),
            ParentSpanId = activity.ParentSpanId == default ? null : activity.ParentSpanId.ToString(),
            Name = activity.DisplayName,
            Kind = activity.Kind.ToString(),
            StartUtc = activity.StartTimeUtc,
            DurationMs = activity.Duration.TotalMilliseconds,
            Status = activity.Status == ActivityStatusCode.Unset ? null : activity.Status.ToString()
        });

        TrimQueue(queue);
    }

    public IReadOnlyList<TraceSpanRecord> GetSpans(string traceId)
    {
        if (!_options.Enabled)
        {
            return Array.Empty<TraceSpanRecord>();
        }

        if (!_spansByTrace.TryGetValue(traceId, out var queue))
        {
            return Array.Empty<TraceSpanRecord>();
        }

        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-_options.MaxSpanAgeSeconds);
        var spans = queue
            .Where(span => span.StartUtc >= cutoff)
            .OrderBy(span => span.StartUtc)
            .ToList();

        return spans;
    }

    private void TrimQueue(ConcurrentQueue<TraceSpanRecord> queue)
    {
        while (queue.Count > _options.MaxSpansPerTrace && queue.TryDequeue(out _))
        {
        }
    }

    private void TrimTraceList()
    {
        while (_traceOrder.Count > _options.MaxTraces && _traceOrder.TryDequeue(out var traceId))
        {
            _spansByTrace.TryRemove(traceId, out _);
        }
    }
}
