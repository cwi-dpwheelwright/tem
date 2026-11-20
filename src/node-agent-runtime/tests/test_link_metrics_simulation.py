from __future__ import annotations

from node_agent_runtime.link_adapters import LabeledLinkMetricsAdapter, StaticLinkMetricsAdapter
from node_agent_runtime.link_collection import collect_link_metrics
from node_agent_runtime.link_metrics import InterfaceMetrics, RadioQualityMetrics


def test_simulated_heterogeneous_interfaces():
    interfaces = [
        InterfaceMetrics(
            interface_id="wifi0",
            interface_type="wifi",
            observed_utc="2026-02-01T00:00:00Z",
            state="up",
            rx_mbps=12.5,
            tx_mbps=3.2,
            mtu=1500,
            quality=RadioQualityMetrics(latency_ms=30.0, jitter_ms=5.0, loss_ratio=0.01),
        ),
        InterfaceMetrics(
            interface_id="cell0",
            interface_type="cellular",
            observed_utc="2026-02-01T00:00:00Z",
            state="up",
            rx_mbps=5.1,
            tx_mbps=1.8,
            mtu=1420,
            quality=RadioQualityMetrics(latency_ms=80.0, jitter_ms=10.0, loss_ratio=0.05),
        ),
        InterfaceMetrics(
            interface_id="sat0",
            interface_type="satellite",
            observed_utc="2026-02-01T00:00:00Z",
            state="up",
            rx_mbps=1.2,
            tx_mbps=0.6,
            mtu=1280,
            quality=RadioQualityMetrics(latency_ms=600.0, jitter_ms=50.0, loss_ratio=0.1),
        ),
    ]
    report = collect_link_metrics(
        "node-1",
        [StaticLinkMetricsAdapter(interfaces)],
        state_dir=None,
    )
    assert len(report.interfaces) == 3
    types = {item.interface_type for item in report.interfaces}
    assert types == {"wifi", "cellular", "satellite"}


def test_labeled_adapter_prefixes_interface_ids():
    interfaces = [
        InterfaceMetrics(
            interface_id="wifi0",
            interface_type="wifi",
            observed_utc="2026-02-01T00:00:00Z",
            state="up",
            rx_mbps=12.5,
            tx_mbps=3.2,
            mtu=1500,
            quality=RadioQualityMetrics(latency_ms=30.0, jitter_ms=5.0, loss_ratio=0.01),
        ),
    ]
    report = collect_link_metrics(
        "node-1",
        [LabeledLinkMetricsAdapter(label="sim", interfaces=interfaces)],
        state_dir=None,
    )
    assert report.interfaces[0].interface_id == "sim:wifi0"
