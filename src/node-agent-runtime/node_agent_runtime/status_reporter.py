"""Status reporting to coordinator + local logs."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from urllib import request
import json

from .config import AgentConfig, StatusConfig, ConfigError
from .health import health_payload


@dataclass(frozen=True)
class StatusReport:
    node_id: str
    observed_utc: str
    payload: dict[str, Any]


def build_status_report(config: AgentConfig) -> StatusReport:
    observed = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    payload = health_payload(config)
    return StatusReport(node_id=config.identity.node_id, observed_utc=observed, payload=payload)


def record_status(report: StatusReport, state_dir: Path | None) -> Path | None:
    if state_dir is None:
        return None
    path = state_dir / "status" / "reports.jsonl"
    path.parent.mkdir(parents=True, exist_ok=True)
    entry = {
        "node_id": report.node_id,
        "observed_utc": report.observed_utc,
        "payload": report.payload,
    }
    with path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(entry, sort_keys=True))
        handle.write("\n")
    return path


def send_status(report: StatusReport, config: StatusConfig) -> bool:
    if not config.status_url:
        raise ConfigError("status.status_url required")
    data = json.dumps(
        {
            "node_id": report.node_id,
            "observed_utc": report.observed_utc,
            "payload": report.payload,
        },
        sort_keys=True,
    ).encode("utf-8")
    req = request.Request(
        config.status_url,
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with request.urlopen(req, timeout=config.timeout_seconds) as resp:
        return 200 <= resp.status < 300
