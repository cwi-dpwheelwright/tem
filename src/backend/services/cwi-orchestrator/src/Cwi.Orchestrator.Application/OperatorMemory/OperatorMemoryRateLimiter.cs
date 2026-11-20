#nullable enable
using System.Collections.Concurrent;

namespace Cwi.Orchestrator.Application.OperatorMemory;

public sealed class OperatorMemoryRateLimiter
{
    private readonly ConcurrentDictionary<string, RateWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly OperatorMemoryOptions _options;

    public OperatorMemoryRateLimiter(OperatorMemoryOptions options)
    {
        _options = options;
    }

    public bool TryConsume(string key, int count, out TimeSpan? retryAfter)
    {
        retryAfter = null;
        if (count <= 0)
        {
            return true;
        }

        var window = _windows.GetOrAdd(key, _ => new RateWindow(DateTimeOffset.UtcNow));
        lock (window.Gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - window.StartUtc >= _options.RateLimitWindow)
            {
                window.StartUtc = now;
                window.Count = 0;
            }

            if (window.Count + count > _options.MaxEventsPerWindow)
            {
                retryAfter = _options.RateLimitWindow - (now - window.StartUtc);
                if (retryAfter < TimeSpan.Zero)
                {
                    retryAfter = TimeSpan.Zero;
                }
                return false;
            }

            window.Count += count;
            return true;
        }
    }

    private sealed class RateWindow
    {
        public RateWindow(DateTimeOffset startUtc)
        {
            StartUtc = startUtc;
        }

        public DateTimeOffset StartUtc { get; set; }
        public int Count { get; set; }
        public object Gate { get; } = new();
    }
}
#nullable disable
