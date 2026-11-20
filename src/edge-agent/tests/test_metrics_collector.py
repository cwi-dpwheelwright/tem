from edge_agent.metrics_collectors import SimulatedMetricsCollector


def test_simulated_metrics_collector_is_deterministic() -> None:
    observed = "2026-02-02T00:00:00Z"
    collector_a = SimulatedMetricsCollector(node_id="edge-node-1", seed=123)
    collector_b = SimulatedMetricsCollector(node_id="edge-node-1", seed=123)

    first = [item.to_dict() for item in collector_a.collect(observed)]
    second = [item.to_dict() for item in collector_b.collect(observed)]

    assert first == second
    assert len(first) == 2
    assert first[0]["interface_id"] == "wifi0"
    assert first[1]["interface_type"] == "lte"
    assert first[0]["observed_utc"] == observed
    assert first[0]["quality"]["latency_ms"] is not None
