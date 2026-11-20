from __future__ import annotations

from dataclasses import dataclass
from typing import Any, Dict, List


@dataclass(frozen=True)
class LinkMetricsQuality:
    latency_ms: float | None = None
    jitter_ms: float | None = None
    loss_ratio: float | None = None
    throughput_mbps: float | None = None
    rssi_dbm: float | None = None
    snr_db: float | None = None

    def to_dict(self) -> Dict[str, Any]:
        return {
            "latency_ms": self.latency_ms,
            "jitter_ms": self.jitter_ms,
            "loss_ratio": self.loss_ratio,
            "throughput_mbps": self.throughput_mbps,
            "rssi_dbm": self.rssi_dbm,
            "snr_db": self.snr_db,
        }


@dataclass(frozen=True)
class LinkMetricsInterface:
    interface_id: str
    interface_type: str
    state: str
    observed_utc: str | None = None
    rx_mbps: float | None = None
    tx_mbps: float | None = None
    mtu: int | None = None
    quality: LinkMetricsQuality | None = None

    def to_dict(self) -> Dict[str, Any]:
        payload: Dict[str, Any] = {
            "interface_id": self.interface_id,
            "interface_type": self.interface_type,
            "state": self.state,
        }
        if self.observed_utc is not None:
            payload["observed_utc"] = self.observed_utc
        if self.rx_mbps is not None:
            payload["rx_mbps"] = self.rx_mbps
        if self.tx_mbps is not None:
            payload["tx_mbps"] = self.tx_mbps
        if self.mtu is not None:
            payload["mtu"] = self.mtu
        if self.quality is not None:
            payload["quality"] = self.quality.to_dict()
        return payload


@dataclass(frozen=True)
class LinkMetricsReport:
    node_id: str
    interfaces: List[LinkMetricsInterface]
    observed_utc: str | None = None

    def to_dict(self) -> Dict[str, Any]:
        payload: Dict[str, Any] = {
            "node_id": self.node_id,
            "interfaces": [interface.to_dict() for interface in self.interfaces],
        }
        if self.observed_utc is not None:
            payload["observed_utc"] = self.observed_utc
        return payload


@dataclass(frozen=True)
class LinkMetricsTransportRequest:
    node_id: str
    interfaces: List[LinkMetricsInterface]
    observed_utc: str | None = None
    sequence: int | None = None
    signature: str | None = None
    key_id: str | None = None

    def to_dict(self) -> Dict[str, Any]:
        payload: Dict[str, Any] = {
            "node_id": self.node_id,
            "interfaces": [interface.to_dict() for interface in self.interfaces],
            "payload": LinkMetricsReport(
                node_id=self.node_id,
                interfaces=self.interfaces,
                observed_utc=self.observed_utc,
            ).to_dict(),
        }
        if self.observed_utc is not None:
            payload["observed_utc"] = self.observed_utc
        if self.sequence is not None:
            payload["sequence"] = self.sequence
        if self.signature is not None:
            payload["signature"] = self.signature
        if self.key_id is not None:
            payload["key_id"] = self.key_id
        return payload
