#nullable enable
using System.Collections.Concurrent;
using System.Threading;

namespace Cwi.Orchestrator.Application.Telemetry;

public sealed class TelemetryIndexStore
{
    private readonly ConcurrentQueue<TelemetryIndexEntry> _entries = new();
    private readonly TelemetryIndexOptions _options;
    private long _count;

    public TelemetryIndexStore(TelemetryIndexOptions options)
    {
        _options = options;
    }

    public void Record(TelemetryIndexEntry entry)
    {
        if (!_options.Enabled || _options.MaxEntries <= 0)
        {
            return;
        }

        _entries.Enqueue(entry);
        Interlocked.Increment(ref _count);
        TrimExcess();
    }

    public IReadOnlyList<TelemetryIndexEntry> GetLatest(string? tenantId, int limit)
    {
        if (!_options.Enabled || _options.MaxEntries <= 0 || limit <= 0)
        {
            return Array.Empty<TelemetryIndexEntry>();
        }

        var snapshot = _entries.ToArray();
        IEnumerable<TelemetryIndexEntry> query = snapshot;

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(entry =>
                string.Equals(entry.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));
        }

        return query
            .OrderByDescending(entry => entry.CreatedUtc)
            .Take(limit)
            .ToList();
    }

    private void TrimExcess()
    {
        while (_count > _options.MaxEntries && _entries.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _count);
        }
    }
}
#nullable disable
