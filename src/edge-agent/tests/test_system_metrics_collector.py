import importlib.util
import os
import time

import pytest

from edge_agent.metrics_collectors import SystemMetricsCollector


@pytest.mark.integration
def test_system_metrics_collector_integration() -> None:
    if not importlib.util.find_spec("psutil"):
        pytest.skip("psutil not installed")
    if not os.getenv("CWI_SYSTEM_COLLECTOR_TEST", ""):
        pytest.skip("CWI_SYSTEM_COLLECTOR_TEST not set")

    collector = SystemMetricsCollector(node_id="edge-node-1")
    observed = "2026-02-02T00:00:00Z"
    first = collector.collect(observed)
    time.sleep(0.1)
    second = collector.collect(observed)

    assert first
    assert second
    assert all(item.interface_id for item in second)
    assert all(item.state in {"up", "down"} for item in second)
    assert any(item.rx_mbps is not None or item.tx_mbps is not None for item in second)
