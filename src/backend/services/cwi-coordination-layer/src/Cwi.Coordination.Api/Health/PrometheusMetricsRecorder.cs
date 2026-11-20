using Cwi.Coordination.Application.Services.Decorators;
using Prometheus;

namespace Cwi.Coordination.Api.Health;

public sealed class PrometheusMetricsRecorder : IMetricsRecorder
{
    private static readonly Counter Counter = Metrics.CreateCounter(
        "coordination_requests_total",
        "Coordination-layer request counters",
        new CounterConfiguration
        {
            LabelNames = new[] { "name" }
        });

    public void Increment(string name)
    {
        Counter.WithLabels(name).Inc();
    }
}
