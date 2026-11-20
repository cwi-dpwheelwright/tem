from __future__ import annotations

import random
import time
from dataclasses import dataclass
from typing import Protocol, List

try:  # pragma: no cover - optional dependency
    import psutil
except Exception:  # pragma: no cover - optional dependency
    psutil = None

from .metrics import LinkMetricsInterface, LinkMetricsQuality


class MetricsCollector(Protocol):
    def collect(self, observed_utc: str) -> List[LinkMetricsInterface]:
        ...


@dataclass
class SimulatedMetricsCollector:
    node_id: str
    seed: int = 42

    def __post_init__(self) -> None:
        self._rng = random.Random(self.seed)

    def collect(self, observed_utc: str) -> List[LinkMetricsInterface]:
        return [
            self._build_interface("wifi0", "wifi", observed_utc),
            self._build_interface("lte0", "lte", observed_utc),
        ]

    def _build_interface(self, interface_id: str, interface_type: str, observed_utc: str) -> LinkMetricsInterface:
        rx = round(self._rng.uniform(5.0, 120.0), 2)
        tx = round(self._rng.uniform(5.0, 120.0), 2)
        latency = round(self._rng.uniform(5.0, 80.0), 2)
        jitter = round(self._rng.uniform(0.5, 12.0), 2)
        loss = round(self._rng.uniform(0.0, 0.05), 4)
        throughput = round(self._rng.uniform(5.0, 150.0), 2)
        rssi = round(self._rng.uniform(-85.0, -35.0), 2)
        snr = round(self._rng.uniform(5.0, 35.0), 2)
        quality = LinkMetricsQuality(
            latency_ms=latency,
            jitter_ms=jitter,
            loss_ratio=loss,
            throughput_mbps=throughput,
            rssi_dbm=rssi,
            snr_db=snr,
        )
        return LinkMetricsInterface(
            interface_id=interface_id,
            interface_type=interface_type,
            state="up",
            observed_utc=observed_utc,
            rx_mbps=rx,
            tx_mbps=tx,
            mtu=1500,
            quality=quality,
        )


class SystemMetricsCollector:
    def __init__(self, node_id: str, include_interfaces: tuple[str, ...] | None = None) -> None:
        self.node_id = node_id
        self._include = set(include_interfaces) if include_interfaces else None
        self._last_sample_time: float | None = None
        self._last_counters: dict[str, tuple[int, int]] = {}

    def collect(self, observed_utc: str) -> List[LinkMetricsInterface]:
        if psutil is None:
            raise RuntimeError("psutil is required for the system metrics collector.")
        stats = psutil.net_if_stats()
        counters = psutil.net_io_counters(pernic=True)
        now = time.monotonic()
        interfaces: List[LinkMetricsInterface] = []
        for name, stat in stats.items():
            if self._include and name not in self._include:
                continue
            rx_mbps, tx_mbps = self._calculate_throughput(name, counters, now)
            interfaces.append(
                LinkMetricsInterface(
                    interface_id=name,
                    interface_type=_infer_interface_type(name),
                    state="up" if stat.isup else "down",
                    observed_utc=observed_utc,
                    rx_mbps=rx_mbps,
                    tx_mbps=tx_mbps,
                    mtu=stat.mtu if stat.mtu > 0 else None,
                    quality=None,
                )
            )
        self._last_sample_time = now
        self._last_counters = {
            name: (counter.bytes_recv, counter.bytes_sent)
            for name, counter in counters.items()
        }
        return interfaces

    def _calculate_throughput(self, name: str, counters, now: float) -> tuple[float | None, float | None]:
        counter = counters.get(name)
        if counter is None or self._last_sample_time is None:
            return None, None
        last = self._last_counters.get(name)
        if not last:
            return None, None
        delta = max(0.0, now - self._last_sample_time)
        if delta <= 0.0:
            return None, None
        rx_delta = max(0, counter.bytes_recv - last[0])
        tx_delta = max(0, counter.bytes_sent - last[1])
        rx_mbps = (rx_delta * 8) / (1_000_000 * delta)
        tx_mbps = (tx_delta * 8) / (1_000_000 * delta)
        return rx_mbps, tx_mbps


def _infer_interface_type(interface_id: str) -> str:
    lowered = interface_id.lower()
    if lowered.startswith(("wl", "wifi", "wlan")):
        return "wifi"
    if "lte" in lowered or "wwan" in lowered or "cell" in lowered:
        return "lte"
    if lowered.startswith(("eth", "en")):
        return "ethernet"
    return "unknown"
