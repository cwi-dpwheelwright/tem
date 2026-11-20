#nullable enable
namespace Cwi.Orchestrator.Application.OperatorMemory;

public sealed class OperatorMemoryOptions
{
    public int MaxSignalsPerBatch { get; init; } = 100;
    public int MaxPerformancePerBatch { get; init; } = 100;
    public int MaxEventsPerWindow { get; init; } = 300;
    public TimeSpan RateLimitWindow { get; init; } = TimeSpan.FromMinutes(1);
    public int MaxHistorySize { get; init; } = 1000;
    public int ProfileCacheSeconds { get; init; } = 60;
    public double CorrectionRateThreshold { get; init; } = 0.2;
    public double CorrelationScoreThreshold { get; init; } = 0.6;
    public double LatencyBaselineMs { get; init; } = 800;
}
#nullable disable
