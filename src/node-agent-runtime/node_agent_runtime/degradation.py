"""Degradation sampling and threshold evaluation."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from urllib import request
import json
import os
import shutil
import uuid

from .config import ConfigError, DegradationConfig, ResilienceConfig
from .offline_queue import OutboundQueue, resolve_queue_dir

try:
    import psutil  # type: ignore
except ImportError:  # pragma: no cover - optional dependency
    psutil = None


@dataclass(frozen=True)
class SystemMetrics:
    observed_utc: str
    load_1: float | None
    cpu_percent: float | None
    disk_free_ratio: float | None
    memory_free_ratio: float | None


@dataclass(frozen=True)
class DegradationSnapshot:
    severity: float
    cpu_severity: float
    disk_severity: float
    memory_severity: float
    details: dict[str, Any]


@dataclass(frozen=True)
class DegradationEvent:
    event_id: str
    node_id: str
    observed_utc: str
    severity: float
    details: dict[str, Any]
    provenance: dict[str, Any] | None


def sample_metrics(state_dir: Path | None) -> SystemMetrics:
    base_path = state_dir or Path.cwd()
    load_1 = _load_average()
    cpu_percent = _cpu_percent()
    disk_free_ratio = _disk_free_ratio(base_path)
    memory_free_ratio = _memory_free_ratio()
    observed = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    return SystemMetrics(
        observed_utc=observed,
        load_1=load_1,
        cpu_percent=cpu_percent,
        disk_free_ratio=disk_free_ratio,
        memory_free_ratio=memory_free_ratio,
    )


def evaluate_degradation(
    metrics: SystemMetrics,
    config: DegradationConfig,
) -> DegradationSnapshot:
    cpu_severity = _cpu_severity(metrics, config)
    disk_severity = _free_ratio_to_severity(
        metrics.disk_free_ratio,
        config.disk_warning_free_ratio,
        config.disk_critical_free_ratio,
    )
    memory_severity = _free_ratio_to_severity(
        metrics.memory_free_ratio,
        config.memory_warning_free_ratio,
        config.memory_critical_free_ratio,
    )
    severity = max(cpu_severity, disk_severity, memory_severity)
    details = {
        "observed_utc": metrics.observed_utc,
        "load_1": metrics.load_1,
        "cpu_percent": metrics.cpu_percent,
        "disk_free_ratio": metrics.disk_free_ratio,
        "memory_free_ratio": metrics.memory_free_ratio,
        "cpu_severity": round(cpu_severity, 3),
        "disk_severity": round(disk_severity, 3),
        "memory_severity": round(memory_severity, 3),
    }
    return DegradationSnapshot(
        severity=severity,
        cpu_severity=cpu_severity,
        disk_severity=disk_severity,
        memory_severity=memory_severity,
        details=details,
    )


def emit_degradation_event(
    metrics: SystemMetrics,
    snapshot: DegradationSnapshot,
    *,
    node_id: str,
    state_dir: Path | None,
) -> DegradationEvent:
    event_id = uuid.uuid4().hex
    metrics_path = _write_metrics_snapshot(metrics, event_id, state_dir)
    event = DegradationEvent(
        event_id=event_id,
        node_id=node_id,
        observed_utc=metrics.observed_utc,
        severity=snapshot.severity,
        details=snapshot.details,
        provenance={"metrics_path": str(metrics_path)} if metrics_path else None,
    )
    _append_event(event, state_dir)
    return event


def event_payload(event: DegradationEvent) -> dict[str, Any]:
    return {
        "event_id": event.event_id,
        "node_id": event.node_id,
        "observed_utc": event.observed_utc,
        "severity": event.severity,
        "details": event.details,
        "provenance": event.provenance,
    }


def send_degradation_event(event: DegradationEvent, config: DegradationConfig) -> bool:
    if not config.transport_url:
        raise ConfigError("degradation.transport_url required for transport")
    payload = event_payload(event)
    data = json.dumps(payload, sort_keys=True).encode("utf-8")
    req = request.Request(
        config.transport_url,
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with request.urlopen(req, timeout=config.transport_timeout_seconds) as resp:
        return 200 <= resp.status < 300


def send_or_queue_degradation_event(
    event: DegradationEvent,
    *,
    degradation: DegradationConfig,
    resilience: ResilienceConfig,
    state_dir: Path | None,
    send_fn=None,
) -> dict[str, Any]:
    if send_fn is None:
        send_fn = lambda ev: send_degradation_event(ev, degradation)
    queue_dir = resolve_queue_dir(state_dir, resilience)
    queue = OutboundQueue(queue_dir, resilience) if queue_dir else None
    try:
        sent = send_fn(event)
        return {"sent": sent, "queued": False, "queue_path": str(queue.queue_path) if queue else None}
    except Exception as exc:
        if queue is None:
            raise
        queue.enqueue(event_payload(event))
        return {
            "sent": False,
            "queued": True,
            "error": str(exc),
            "queue_path": str(queue.queue_path),
        }


def _load_average() -> float | None:
    try:
        return os.getloadavg()[0]
    except (AttributeError, OSError):
        return None


def _cpu_percent() -> float | None:
    if psutil is None:
        return None
    try:
        return float(psutil.cpu_percent(interval=None))
    except Exception:
        return None


def _memory_free_ratio() -> float | None:
    if psutil is None:
        return None
    try:
        memory = psutil.virtual_memory()
    except Exception:
        return None
    if memory.total <= 0:
        return None
    return float(memory.available) / float(memory.total)


def _disk_free_ratio(path: Path) -> float | None:
    try:
        usage = shutil.disk_usage(path)
    except (FileNotFoundError, OSError):
        return None
    if usage.total <= 0:
        return None
    return usage.free / usage.total


def _load_to_severity(load: float | None, warning: float, critical: float) -> float:
    if load is None:
        return 0.0
    if load >= critical:
        return 1.0
    if load >= warning:
        return 0.7
    return 0.0


def _percent_to_severity(percent: float | None, warning: float, critical: float) -> float:
    if percent is None:
        return 0.0
    if percent >= critical:
        return 1.0
    if percent >= warning:
        return 0.7
    return 0.0


def _cpu_severity(metrics: SystemMetrics, config: DegradationConfig) -> float:
    if metrics.load_1 is not None:
        return _load_to_severity(
            metrics.load_1,
            config.cpu_warning_load,
            config.cpu_critical_load,
        )
    return _percent_to_severity(
        metrics.cpu_percent,
        config.cpu_warning_percent,
        config.cpu_critical_percent,
    )


def _free_ratio_to_severity(free_ratio: float | None, warning: float, critical: float) -> float:
    if free_ratio is None:
        return 0.0
    if free_ratio <= critical:
        return 0.95
    if free_ratio <= warning:
        return 0.7
    return 0.0


def _write_metrics_snapshot(
    metrics: SystemMetrics, event_id: str, state_dir: Path | None
) -> Path | None:
    if state_dir is None:
        return None
    metrics_dir = state_dir / "degradation" / "metrics"
    metrics_dir.mkdir(parents=True, exist_ok=True)
    path = metrics_dir / f"{event_id}.json"
    payload = {
        "observed_utc": metrics.observed_utc,
        "load_1": metrics.load_1,
        "cpu_percent": metrics.cpu_percent,
        "disk_free_ratio": metrics.disk_free_ratio,
        "memory_free_ratio": metrics.memory_free_ratio,
    }
    path.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")
    return path


def _append_event(event: DegradationEvent, state_dir: Path | None) -> None:
    if state_dir is None:
        return
    events_path = state_dir / "degradation" / "events.jsonl"
    events_path.parent.mkdir(parents=True, exist_ok=True)
    payload = event_payload(event)
    with events_path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(payload, sort_keys=True))
        handle.write("\n")
