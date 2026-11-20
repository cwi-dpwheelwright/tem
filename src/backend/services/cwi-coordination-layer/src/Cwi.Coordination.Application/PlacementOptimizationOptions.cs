namespace Cwi.Coordination.Application;

public sealed class PlacementOptimizationOptions
{
    public int MaxMetricsLookback { get; set; } = 200;
    public double MinConfidence { get; set; } = 0.6;
    public bool RequireGovernanceApproval { get; set; }
    public string PolicyVersion { get; set; } = "1.0";
    public string ModelVersion { get; set; } = "heuristic-v1";
}
