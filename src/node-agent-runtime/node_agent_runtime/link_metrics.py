"""Normalized link metrics schema + aggregation."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any


@dataclass(frozen=True)
class RadioQualityMetrics:
    latency_ms: float | None = None
    jitter_ms: float | None = None
    loss_ratio: float | None = None
    throughput_mbps: float | None = None
    rssi_dbm: float | None = None
    snr_db: float | None = None


@dataclass(frozen=True)
class InterfaceMetrics:
    interface_id: str
    interface_type: str
    observed_utc: str
    state: str
    rx_mbps: float | None
    tx_mbps: float | None
    mtu: int | None
    quality: RadioQualityMetrics


@dataclass(frozen=True)
class LinkMetricsReport:
    node_id: str
    observed_utc: str
    interfaces: list[InterfaceMetrics]


def normalize_quality(metrics: RadioQualityMetrics) -> RadioQualityMetrics:
    return RadioQualityMetrics(
        latency_ms=_clamp_non_negative(metrics.latency_ms),
        jitter_ms=_clamp_non_negative(metrics.jitter_ms),
        loss_ratio=_clamp_ratio(metrics.loss_ratio),
        throughput_mbps=_clamp_non_negative(metrics.throughput_mbps),
        rssi_dbm=metrics.rssi_dbm,
        snr_db=metrics.snr_db,
    )


def report_payload(report: LinkMetricsReport) -> dict[str, Any]:
    return {
        "node_id": report.node_id,
        "observed_utc": report.observed_utc,
        "interfaces": [interface_payload(item) for item in report.interfaces],
    }


def interface_payload(item: InterfaceMetrics) -> dict[str, Any]:
    return {
        "interface_id": item.interface_id,
        "interface_type": item.interface_type,
        "observed_utc": item.observed_utc,
        "state": item.state,
        "rx_mbps": item.rx_mbps,
        "tx_mbps": item.tx_mbps,
        "mtu": item.mtu,
        "quality": {
            "latency_ms": item.quality.latency_ms,
            "jitter_ms": item.quality.jitter_ms,
            "loss_ratio": item.quality.loss_ratio,
            "throughput_mbps": item.quality.throughput_mbps,
            "rssi_dbm": item.quality.rssi_dbm,
            "snr_db": item.quality.snr_db,
        },
    }


def build_report(node_id: str, interfaces: list[InterfaceMetrics]) -> LinkMetricsReport:
    observed = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    normalized = [
        InterfaceMetrics(
            interface_id=item.interface_id,
            interface_type=item.interface_type,
            observed_utc=item.observed_utc,
            state=item.state,
            rx_mbps=item.rx_mbps,
            tx_mbps=item.tx_mbps,
            mtu=item.mtu,
            quality=normalize_quality(item.quality),
        )
        for item in interfaces
    ]
    return LinkMetricsReport(node_id=node_id, observed_utc=observed, interfaces=normalized)


def link_metrics_schema() -> dict[str, Any]:
    return {
        "type": "object",
        "required": ["node_id", "observed_utc", "interfaces"],
        "properties": {
            "node_id": {"type": "string"},
            "observed_utc": {"type": "string"},
            "interfaces": {
                "type": "array",
                "items": {
                    "type": "object",
                    "required": ["interface_id", "interface_type", "observed_utc", "state", "quality"],
                    "properties": {
                        "interface_id": {"type": "string"},
                        "interface_type": {"type": "string"},
                        "observed_utc": {"type": "string"},
                        "state": {"type": "string"},
                        "rx_mbps": {"type": ["number", "null"]},
                        "tx_mbps": {"type": ["number", "null"]},
                        "mtu": {"type": ["number", "null"]},
                        "quality": {
                            "type": "object",
                            "properties": {
                                "latency_ms": {"type": ["number", "null"]},
                                "jitter_ms": {"type": ["number", "null"]},
                                "loss_ratio": {"type": ["number", "null"]},
                                "throughput_mbps": {"type": ["number", "null"]},
                                "rssi_dbm": {"type": ["number", "null"]},
                                "snr_db": {"type": ["number", "null"]},
                            },
                        },
                    },
                },
            },
        },
    }


def _clamp_non_negative(value: float | None) -> float | None:
    if value is None:
        return None
    return max(0.0, value)


def _clamp_ratio(value: float | None) -> float | None:
    if value is None:
        return None
    return min(1.0, max(0.0, value))
