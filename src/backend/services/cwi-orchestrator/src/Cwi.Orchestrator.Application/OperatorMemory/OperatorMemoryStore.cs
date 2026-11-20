#nullable enable
using System.Collections.Concurrent;
using System.Linq;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.OperatorMemory;

public sealed class OperatorMemoryStore
{
    private readonly ConcurrentDictionary<string, OperatorMemoryRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly OperatorMemoryOptions _options;

    public OperatorMemoryStore(OperatorMemoryOptions options)
    {
        _options = options;
    }

    public OperatorProfileSummaryDto AppendBatch(OperatorMemoryIngestRequestDto request)
    {
        var key = BuildKey(request.TenantId, request.OperatorId);
        var record = _records.GetOrAdd(key, _ => new OperatorMemoryRecord(request.TenantId, request.OperatorId));

        lock (record.Gate)
        {
            record.Signals.AddRange(request.Signals);
            record.Performance.AddRange(request.Performance);
            record.LastUpdatedUtc = DateTimeOffset.UtcNow;
            TrimToLimit(record.Signals, _options.MaxHistorySize);
            TrimToLimit(record.Performance, _options.MaxHistorySize);
            return BuildProfile(record);
        }
    }

    public OperatorProfileSummaryDto? GetProfile(string tenantId, string operatorId)
    {
        var key = BuildKey(tenantId, operatorId);
        if (!_records.TryGetValue(key, out var record))
        {
            return null;
        }

        lock (record.Gate)
        {
            return BuildProfile(record);
        }
    }

    private OperatorProfileSummaryDto BuildProfile(OperatorMemoryRecord record)
    {
        var signalsTotal = record.Signals.Count;
        var corrections = record.Signals.Count(signal => signal.SignalType == "correction");
        var delays = record.Signals.Count(signal => signal.SignalType == "delay");
        var confirmations = record.Signals.Count(signal => signal.SignalType == "confirmation");
        var correctionRate = signalsTotal == 0 ? 0.0 : corrections / (double)signalsTotal;
        var delayRate = signalsTotal == 0 ? 0.0 : delays / (double)signalsTotal;
        var confirmationRate = signalsTotal == 0 ? 0.0 : confirmations / (double)signalsTotal;

        var latencySamples = record.Performance
            .Where(metric => metric.Metric is "response_latency_ms" or "latency_ms")
            .Select(metric => metric.Value)
            .ToList();
        double? avgLatency = latencySamples.Count == 0 ? null : latencySamples.Average();

        var latencyFactor = avgLatency is null
            ? 0.0
            : Math.Clamp(avgLatency.Value / _options.LatencyBaselineMs, 0.0, 1.0);

        var correlationPenalty = (correctionRate * 0.5) + (delayRate * 0.3) + (latencyFactor * 0.2);
        var correlationScore = Math.Clamp(1.0 - correlationPenalty, 0.0, 1.0);

        return new OperatorProfileSummaryDto
        {
            TenantId = record.TenantId,
            OperatorId = record.OperatorId,
            SignalsTotal = signalsTotal,
            CorrectionRate = Math.Round(correctionRate, 4),
            DelayRate = Math.Round(delayRate, 4),
            ConfirmationRate = Math.Round(confirmationRate, 4),
            AverageLatencyMs = avgLatency is null ? null : Math.Round(avgLatency.Value, 2),
            CorrelationScore = Math.Round(correlationScore, 4),
            LastUpdatedUtc = record.LastUpdatedUtc
        };
    }

    private static void TrimToLimit<T>(List<T> items, int limit)
    {
        if (limit <= 0)
        {
            items.Clear();
            return;
        }

        if (items.Count <= limit)
        {
            return;
        }

        var removeCount = items.Count - limit;
        items.RemoveRange(0, removeCount);
    }

    private static string BuildKey(string tenantId, string operatorId)
    {
        return $"{tenantId.Trim().ToLowerInvariant()}::{operatorId.Trim().ToLowerInvariant()}";
    }

    private sealed class OperatorMemoryRecord
    {
        public OperatorMemoryRecord(string tenantId, string operatorId)
        {
            TenantId = tenantId;
            OperatorId = operatorId;
        }

        public string TenantId { get; }
        public string OperatorId { get; }
        public List<OperatorSignalDto> Signals { get; } = new();
        public List<OperatorPerformanceDto> Performance { get; } = new();
        public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
        public object Gate { get; } = new();
    }
}
#nullable disable
