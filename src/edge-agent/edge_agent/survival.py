from __future__ import annotations

import json
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict
import shutil

from .backoff import CircuitBreaker
from .buffer import JsonlBuffer
from .config import EdgeAgentConfig
from .observability import Observability
from .survival_client import SurvivalSignalClient


@dataclass(frozen=True)
class DegradationSnapshot:
    severity: float
    resource_severity: float
    network_severity: float
    details: str


class SurvivalSignalEmitter:
    def __init__(
        self,
        client: SurvivalSignalClient,
        config: EdgeAgentConfig,
        observability: Observability,
    ) -> None:
        self.client = client
        self.config = config
        self.observability = observability
        self._last_emit = 0.0
        self._last_severity: float | None = None

    def emit_if_needed(self, snapshot: DegradationSnapshot) -> None:
        if not self.config.survival_enabled:
            return
        if snapshot.severity < self.config.survival_min_severity:
            return

        now = time.time()
        if self._last_emit and (now - self._last_emit) < self.config.survival_emit_interval_seconds:
            return
        if (
            self._last_severity is not None
            and abs(snapshot.severity - self._last_severity) < self.config.survival_min_change
        ):
            return

        payload = {
            "signal_type": self.config.survival_signal_type,
            "severity": snapshot.severity,
            "details": snapshot.details,
            "observed_utc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        }
        result = self.client.send_signal(payload)
        if result.ok:
            self._last_emit = now
            self._last_severity = snapshot.severity
            self.observability.record_degradation(
                snapshot.severity, snapshot.resource_severity, snapshot.network_severity
            )


def compute_degradation(
    buffer: JsonlBuffer,
    breaker: CircuitBreaker,
    config: EdgeAgentConfig,
) -> DegradationSnapshot:
    stats = buffer.stats()
    resource_severity, resource_details = _resource_severity(stats, buffer, config)
    network_severity, network_details = _network_severity(breaker, config)
    severity = max(resource_severity, network_severity)
    details = {
        "buffer_pending": stats.pending_count,
        "buffer_bytes": stats.size_bytes,
        "resource_severity": round(resource_severity, 3),
        "network_severity": round(network_severity, 3),
        "breaker_status": breaker.status(),
        "failure_count": breaker.failure_count,
        "resource_details": resource_details,
        "network_details": network_details,
    }
    return DegradationSnapshot(
        severity=severity,
        resource_severity=resource_severity,
        network_severity=network_severity,
        details=json.dumps(details, sort_keys=True),
    )


def _resource_severity(stats, buffer: JsonlBuffer, config: EdgeAgentConfig) -> tuple[float, Dict[str, float]]:
    ratios: Dict[str, float] = {}
    if buffer.max_items:
        ratios["buffer_items_ratio"] = stats.pending_count / buffer.max_items
    if buffer.max_bytes:
        ratios["buffer_bytes_ratio"] = stats.size_bytes / buffer.max_bytes
    disk_ratio = _disk_used_ratio(Path(buffer.buffer_path))
    if disk_ratio is not None:
        ratios["disk_used_ratio"] = disk_ratio

    severity = 0.0
    if "buffer_items_ratio" in ratios:
        severity = max(
            severity,
            _ratio_to_severity(
                ratios["buffer_items_ratio"],
                config.degradation_buffer_warning_ratio,
                config.degradation_buffer_critical_ratio,
            ),
        )
    if "buffer_bytes_ratio" in ratios:
        severity = max(
            severity,
            _ratio_to_severity(
                ratios["buffer_bytes_ratio"],
                config.degradation_buffer_warning_ratio,
                config.degradation_buffer_critical_ratio,
            ),
        )
    if "disk_used_ratio" in ratios:
        free_ratio = 1.0 - ratios["disk_used_ratio"]
        severity = max(
            severity,
            _free_ratio_to_severity(
                free_ratio,
                config.degradation_disk_warning_free_ratio,
                config.degradation_disk_critical_free_ratio,
            ),
        )
    return severity, ratios


def _network_severity(breaker: CircuitBreaker, config: EdgeAgentConfig) -> tuple[float, Dict[str, float]]:
    status = breaker.status()
    severity = 0.0
    if status == "open":
        severity = config.degradation_network_open_severity
    elif status == "half-open":
        severity = config.degradation_network_half_open_severity
    elif breaker.failure_count >= config.degradation_network_failure_threshold:
        severity = config.degradation_network_half_open_severity
    return severity, {"breaker_status": 1.0 if status == "open" else 0.0}


def _ratio_to_severity(ratio: float, warning: float, critical: float) -> float:
    if ratio >= critical:
        return max(0.0, min(1.0, ratio))
    if ratio >= warning:
        return warning
    return 0.0


def _free_ratio_to_severity(free_ratio: float, warning: float, critical: float) -> float:
    if free_ratio <= critical:
        return 0.95
    if free_ratio <= warning:
        return 0.7
    return 0.0


def _disk_used_ratio(path: Path) -> float | None:
    try:
        usage = shutil.disk_usage(path)
    except (FileNotFoundError, OSError):
        return None
    if usage.total <= 0:
        return None
    return usage.used / usage.total
