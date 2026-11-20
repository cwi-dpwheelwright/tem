"""Link metrics adapters."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Protocol
import json

try:
    import psutil  # type: ignore
except ImportError:  # pragma: no cover - optional dependency
    psutil = None

from .config import MetricsIngestionConfig, MetricsSigningConfig
from .link_metrics import InterfaceMetrics, RadioQualityMetrics
from .metrics_ingestion import ingest_metrics_envelope, parse_external_interfaces


class LinkMetricsAdapter(Protocol):
    def collect(self, state_dir: Path | None) -> list[InterfaceMetrics]:
        ...


@dataclass(frozen=True)
class StaticLinkMetricsAdapter:
    interfaces: list[InterfaceMetrics]

    def collect(self, state_dir: Path | None) -> list[InterfaceMetrics]:
        return self.interfaces


@dataclass(frozen=True)
class ExternalLinkMetricsAdapter:
    envelope_payload: dict[str, Any]
    signing: MetricsSigningConfig
    ingestion: MetricsIngestionConfig

    def collect(self, state_dir: Path | None) -> list[InterfaceMetrics]:
        payload = ingest_metrics_envelope(
            self.envelope_payload,
            signing=self.signing,
            policy=self.ingestion,
            state_dir=state_dir,
        )
        return parse_external_interfaces(payload)


class OsLinkMetricsAdapter:
    def __init__(self) -> None:
        if psutil is None:
            raise RuntimeError("psutil required for OS adapter")

    def collect(self, state_dir: Path | None) -> list[InterfaceMetrics]:
        stats = psutil.net_if_stats()
        counters = psutil.net_io_counters(pernic=True)
        observed = _utc_now()
        prev = _load_prev_counters(state_dir)
        current = {}
        interfaces: list[InterfaceMetrics] = []
        for name, stat in stats.items():
            counter = counters.get(name)
            rx_mbps = tx_mbps = None
            if counter:
                current[name] = {
                    "rx_bytes": counter.bytes_recv,
                    "tx_bytes": counter.bytes_sent,
                    "observed_utc": observed,
                }
                if name in prev:
                    rx_mbps, tx_mbps = _compute_throughput(prev[name], current[name])
            interfaces.append(
                InterfaceMetrics(
                    interface_id=name,
                    interface_type=_infer_type(name),
                    observed_utc=observed,
                    state="up" if stat.isup else "down",
                    rx_mbps=rx_mbps,
                    tx_mbps=tx_mbps,
                    mtu=stat.mtu,
                    quality=RadioQualityMetrics(),
                )
            )
        _save_prev_counters(state_dir, current)
        return interfaces


@dataclass(frozen=True)
class LabeledLinkMetricsAdapter:
    label: str
    interfaces: list[InterfaceMetrics]

    def collect(self, state_dir: Path | None) -> list[InterfaceMetrics]:
        labeled: list[InterfaceMetrics] = []
        for item in self.interfaces:
            labeled.append(
                InterfaceMetrics(
                    interface_id=f"{self.label}:{item.interface_id}",
                    interface_type=item.interface_type,
                    observed_utc=item.observed_utc,
                    state=item.state,
                    rx_mbps=item.rx_mbps,
                    tx_mbps=item.tx_mbps,
                    mtu=item.mtu,
                    quality=item.quality,
                )
            )
        return labeled


def _infer_type(name: str) -> str:
    lowered = name.lower()
    if lowered.startswith(("wl", "wifi", "wlan")):
        return "wifi"
    if lowered.startswith(("wwan", "cell", "lte", "5g")):
        return "cellular"
    if lowered.startswith(("sat", "iridium")):
        return "satellite"
    if lowered.startswith(("eth", "en")):
        return "ethernet"
    return "unknown"


def _load_prev_counters(state_dir: Path | None) -> dict[str, dict[str, str | int]]:
    if state_dir is None:
        return {}
    path = state_dir / "link_metrics" / "iface_counters.json"
    if not path.exists():
        return {}
    raw = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(raw, dict):
        return {}
    return {name: value for name, value in raw.items() if isinstance(value, dict)}


def _save_prev_counters(state_dir: Path | None, counters: dict[str, dict[str, object]]) -> None:
    if state_dir is None:
        return
    path = state_dir / "link_metrics" / "iface_counters.json"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(counters, indent=2, sort_keys=True), encoding="utf-8")


def _compute_throughput(prev: dict[str, object], current: dict[str, object]) -> tuple[float | None, float | None]:
    try:
        prev_rx = int(prev.get("rx_bytes", 0))
        prev_tx = int(prev.get("tx_bytes", 0))
        curr_rx = int(current.get("rx_bytes", 0))
        curr_tx = int(current.get("tx_bytes", 0))
        prev_time = _parse_time(prev.get("observed_utc"))
        curr_time = _parse_time(current.get("observed_utc"))
    except Exception:
        return None, None
    delta = (curr_time - prev_time).total_seconds()
    if delta <= 0:
        return None, None
    rx_mbps = (max(curr_rx - prev_rx, 0) * 8) / (delta * 1_000_000)
    tx_mbps = (max(curr_tx - prev_tx, 0) * 8) / (delta * 1_000_000)
    return round(rx_mbps, 3), round(tx_mbps, 3)


def _parse_time(value: object) -> datetime:
    if not isinstance(value, str):
        raise ValueError("invalid time")
    cleaned = value.replace("Z", "+00:00")
    parsed = datetime.fromisoformat(cleaned)
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def _utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
