using System.Collections.Concurrent;

namespace Cwi.Orchestrator.Application.Feedback;

public sealed class OutcomeFeedbackProfileSignal
{
    public string TenantId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public string? TicketId { get; init; }
    public string? ActionId { get; init; }
    public string ActorId { get; init; } = string.Empty;
    public string Feedback { get; init; } = string.Empty;
    public int UpCount { get; init; }
    public int DownCount { get; init; }
    public string? LastFeedback { get; init; }
    public DateTime? LastFeedbackUtc { get; init; }
    public DateTimeOffset RecordedUtc { get; init; }
}

public sealed class OutcomeFeedbackSignalStore
{
    private readonly ConcurrentQueue<OutcomeFeedbackProfileSignal> _signals = new();
    private const int MaxSignals = 1000;

    public void Record(OutcomeFeedbackProfileSignal signal)
    {
        _signals.Enqueue(signal);
        while (_signals.Count > MaxSignals && _signals.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<OutcomeFeedbackProfileSignal> GetLatest(string? tenantId, int limit)
    {
        if (limit <= 0)
        {
            return Array.Empty<OutcomeFeedbackProfileSignal>();
        }

        var snapshot = _signals.ToArray();
        IEnumerable<OutcomeFeedbackProfileSignal> query = snapshot;
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(signal => string.Equals(signal.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));
        }

        return query
            .OrderByDescending(signal => signal.RecordedUtc)
            .Take(limit)
            .ToList();
    }
}
