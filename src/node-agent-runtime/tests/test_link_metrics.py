from __future__ import annotations

from node_agent_runtime.link_metrics import (
    InterfaceMetrics,
    RadioQualityMetrics,
    build_report,
    link_metrics_schema,
)


def test_link_metrics_schema_has_quality_fields():
    schema = link_metrics_schema()
    iface = schema["properties"]["interfaces"]["items"]["properties"]
    quality = iface["quality"]["properties"]
    assert "latency_ms" in quality
    assert "jitter_ms" in quality
    assert "loss_ratio" in quality
    assert "throughput_mbps" in quality
    assert "rssi_dbm" in quality
    assert "snr_db" in quality


def test_build_report_normalizes_values():
    item = InterfaceMetrics(
        interface_id="eth0",
        interface_type="ethernet",
        observed_utc="2026-02-01T00:00:00Z",
        state="up",
        rx_mbps=-1.0,
        tx_mbps=1.0,
        mtu=1500,
        quality=RadioQualityMetrics(
            latency_ms=-5.0,
            jitter_ms=-1.0,
            loss_ratio=2.0,
            throughput_mbps=-10.0,
        ),
    )
    report = build_report("node-1", [item])
    normalized = report.interfaces[0].quality
    assert normalized.latency_ms == 0.0
    assert normalized.jitter_ms == 0.0
    assert normalized.loss_ratio == 1.0
    assert normalized.throughput_mbps == 0.0
