#nullable enable
using System.Linq;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.OperatorMemory;

public sealed class OperatorMemoryRedactionPolicy
{
    private static readonly HashSet<string> AllowedMetadataKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "device_id",
        "client",
        "mode",
        "locale",
        "shift",
        "site_id"
    };

    private readonly OperatorMemoryOptions _options;

    public OperatorMemoryRedactionPolicy(OperatorMemoryOptions options)
    {
        _options = options;
    }

    public OperatorMemoryIngestRequestDto Sanitize(OperatorMemoryIngestRequestDto request)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (request.Metadata is not null)
        {
            foreach (var pair in request.Metadata)
            {
                if (!AllowedMetadataKeys.Contains(pair.Key))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                metadata[pair.Key] = pair.Value.Trim();
            }
        }

        var signals = request.Signals
            .Where(signal => !string.IsNullOrWhiteSpace(signal.SignalType))
            .Take(_options.MaxSignalsPerBatch)
            .Select(signal => new OperatorSignalDto
            {
                SignalType = signal.SignalType.Trim().ToLowerInvariant(),
                Value = signal.Value,
                Unit = signal.Unit?.Trim() ?? string.Empty,
                Confidence = signal.Confidence,
                OccurredUtc = signal.OccurredUtc == default ? DateTimeOffset.UtcNow : signal.OccurredUtc
            })
            .ToList();

        var performance = request.Performance
            .Where(metric => !string.IsNullOrWhiteSpace(metric.Metric))
            .Take(_options.MaxPerformancePerBatch)
            .Select(metric => new OperatorPerformanceDto
            {
                Metric = metric.Metric.Trim().ToLowerInvariant(),
                Value = metric.Value,
                Unit = metric.Unit?.Trim() ?? string.Empty,
                Confidence = metric.Confidence,
                WindowUtc = metric.WindowUtc == default ? DateTimeOffset.UtcNow : metric.WindowUtc
            })
            .ToList();

        return new OperatorMemoryIngestRequestDto
        {
            TenantId = request.TenantId?.Trim() ?? string.Empty,
            OperatorId = request.OperatorId?.Trim() ?? string.Empty,
            SessionId = string.IsNullOrWhiteSpace(request.SessionId) ? null : request.SessionId.Trim(),
            Consent = request.Consent is null
                ? null
                : new OperatorConsentDto
                {
                    Granted = request.Consent.Granted,
                    GrantedUtc = request.Consent.GrantedUtc ?? DateTimeOffset.UtcNow,
                    Source = request.Consent.Source?.Trim()
                },
            Signals = signals,
            Performance = performance,
            Metadata = metadata.Count == 0 ? null : metadata
        };
    }
}
#nullable disable
