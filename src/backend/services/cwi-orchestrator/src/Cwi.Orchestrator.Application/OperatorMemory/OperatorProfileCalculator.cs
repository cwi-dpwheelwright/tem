#nullable enable
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Application.OperatorMemory;

internal static class OperatorProfileCalculator
{
    public static OperatorProfile ApplyBatch(
        OperatorProfile? existing,
        OperatorMemoryIngestRequestDto request,
        OperatorMemoryOptions options)
    {
        var now = DateTimeOffset.UtcNow;
        var profile = existing ?? new OperatorProfile
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId.Trim(),
            OperatorId = request.OperatorId.Trim(),
            SignalsTotal = 0,
            CorrectionsTotal = 0,
            DelaysTotal = 0,
            ConfirmationsTotal = 0,
            LatencySum = 0,
            LatencyCount = 0,
            CorrelationScore = 1.0,
            LastUpdatedUtc = now
        };

        profile.TenantId = request.TenantId.Trim();
        profile.OperatorId = request.OperatorId.Trim();

        foreach (var signal in request.Signals)
        {
            profile.SignalsTotal += 1;
            var signalType = signal.SignalType?.Trim();
            if (string.Equals(signalType, "correction", StringComparison.OrdinalIgnoreCase))
            {
                profile.CorrectionsTotal += 1;
            }
            else if (string.Equals(signalType, "delay", StringComparison.OrdinalIgnoreCase))
            {
                profile.DelaysTotal += 1;
            }
            else if (string.Equals(signalType, "confirmation", StringComparison.OrdinalIgnoreCase))
            {
                profile.ConfirmationsTotal += 1;
            }
        }

        foreach (var metric in request.Performance)
        {
            var metricName = metric.Metric?.Trim();
            if (string.Equals(metricName, "response_latency_ms", StringComparison.OrdinalIgnoreCase)
                || string.Equals(metricName, "latency_ms", StringComparison.OrdinalIgnoreCase))
            {
                profile.LatencySum += metric.Value;
                profile.LatencyCount += 1;
            }
        }

        CapSignals(profile, options.MaxHistorySize);
        CapLatency(profile, options.MaxHistorySize);

        profile.LastUpdatedUtc = now;
        profile.CorrelationScore = ComputeCorrelation(profile, options);
        return profile;
    }

    public static OperatorProfileSummaryDto ToSummary(OperatorProfile profile, OperatorMemoryOptions options)
    {
        var signalsTotal = profile.SignalsTotal;
        var correctionRate = signalsTotal == 0 ? 0.0 : profile.CorrectionsTotal / (double)signalsTotal;
        var delayRate = signalsTotal == 0 ? 0.0 : profile.DelaysTotal / (double)signalsTotal;
        var confirmationRate = signalsTotal == 0 ? 0.0 : profile.ConfirmationsTotal / (double)signalsTotal;
        double? avgLatency = profile.LatencyCount == 0 ? null : profile.LatencySum / profile.LatencyCount;

        return new OperatorProfileSummaryDto
        {
            TenantId = profile.TenantId,
            OperatorId = profile.OperatorId,
            SignalsTotal = signalsTotal,
            CorrectionRate = Math.Round(correctionRate, 4),
            DelayRate = Math.Round(delayRate, 4),
            ConfirmationRate = Math.Round(confirmationRate, 4),
            AverageLatencyMs = avgLatency is null ? null : Math.Round(avgLatency.Value, 2),
            CorrelationScore = Math.Round(profile.CorrelationScore, 4),
            LastUpdatedUtc = profile.LastUpdatedUtc
        };
    }

    private static double ComputeCorrelation(OperatorProfile profile, OperatorMemoryOptions options)
    {
        var signalsTotal = profile.SignalsTotal;
        var correctionRate = signalsTotal == 0 ? 0.0 : profile.CorrectionsTotal / (double)signalsTotal;
        var delayRate = signalsTotal == 0 ? 0.0 : profile.DelaysTotal / (double)signalsTotal;
        double? avgLatency = profile.LatencyCount == 0 ? null : profile.LatencySum / profile.LatencyCount;
        var latencyFactor = avgLatency is null
            ? 0.0
            : Math.Clamp(avgLatency.Value / options.LatencyBaselineMs, 0.0, 1.0);

        var correlationPenalty = (correctionRate * 0.5) + (delayRate * 0.3) + (latencyFactor * 0.2);
        return Math.Clamp(1.0 - correlationPenalty, 0.0, 1.0);
    }

    private static void CapSignals(OperatorProfile profile, int max)
    {
        if (max <= 0)
        {
            profile.SignalsTotal = 0;
            profile.CorrectionsTotal = 0;
            profile.DelaysTotal = 0;
            profile.ConfirmationsTotal = 0;
            return;
        }

        if (profile.SignalsTotal <= max)
        {
            return;
        }

        var scale = max / (double)profile.SignalsTotal;
        profile.SignalsTotal = max;
        profile.CorrectionsTotal = (int)Math.Round(profile.CorrectionsTotal * scale);
        profile.DelaysTotal = (int)Math.Round(profile.DelaysTotal * scale);
        profile.ConfirmationsTotal = (int)Math.Round(profile.ConfirmationsTotal * scale);
    }

    private static void CapLatency(OperatorProfile profile, int max)
    {
        if (max <= 0)
        {
            profile.LatencyCount = 0;
            profile.LatencySum = 0;
            return;
        }

        if (profile.LatencyCount <= max)
        {
            return;
        }

        var scale = max / (double)profile.LatencyCount;
        profile.LatencyCount = max;
        profile.LatencySum *= scale;
    }
}
#nullable disable
