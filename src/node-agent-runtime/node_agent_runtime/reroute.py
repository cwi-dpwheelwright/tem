"""Reroute hint evaluation + transport."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone, timedelta
from pathlib import Path
from typing import Any
from urllib import request
import json

from .config import ConfigError, RerouteConfig


@dataclass(frozen=True)
class RerouteHint:
    node_id: str
    observed_utc: str
    severity: float
    action: str
    reason: str


def evaluate_reroute_hint(
    *,
    node_id: str,
    severity: float,
    config: RerouteConfig,
    state_dir: Path | None,
) -> RerouteHint | None:
    state = _load_state(state_dir)
    observed = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")

    if state.get("cooldown_until") and _parse_time(state["cooldown_until"]) > datetime.now(timezone.utc):
        return None

    active = bool(state.get("active", False))
    if not active and severity >= config.critical_threshold:
        hint = RerouteHint(
            node_id=node_id,
            observed_utc=observed,
            severity=severity,
            action="activate",
            reason="critical_threshold",
        )
        _save_state(state_dir, {"active": True, "last_action": "activate", "cooldown_until": _cooldown_until(config)})
        return hint

    if active and severity <= config.clear_threshold:
        hint = RerouteHint(
            node_id=node_id,
            observed_utc=observed,
            severity=severity,
            action="clear",
            reason="clear_threshold",
        )
        _save_state(state_dir, {"active": False, "last_action": "clear", "cooldown_until": _cooldown_until(config)})
        return hint

    return None


def hint_payload(hint: RerouteHint) -> dict[str, Any]:
    return {
        "node_id": hint.node_id,
        "observed_utc": hint.observed_utc,
        "severity": hint.severity,
        "action": hint.action,
        "reason": hint.reason,
    }


def send_reroute_hint(hint: RerouteHint, config: RerouteConfig) -> bool:
    if not config.transport_url:
        raise ConfigError("reroute.transport_url required")
    data = json.dumps(hint_payload(hint), sort_keys=True).encode("utf-8")
    req = request.Request(
        config.transport_url,
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with request.urlopen(req, timeout=config.transport_timeout_seconds) as resp:
        return 200 <= resp.status < 300


def _state_path(state_dir: Path | None) -> Path | None:
    if state_dir is None:
        return None
    return state_dir / "reroute" / "state.json"


def _load_state(state_dir: Path | None) -> dict[str, Any]:
    path = _state_path(state_dir)
    if path is None or not path.exists():
        return {}
    raw = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(raw, dict):
        return {}
    return raw


def _save_state(state_dir: Path | None, state: dict[str, Any]) -> None:
    path = _state_path(state_dir)
    if path is None:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(state, indent=2, sort_keys=True), encoding="utf-8")


def _cooldown_until(config: RerouteConfig) -> str | None:
    if config.cooldown_seconds <= 0:
        return None
    return (datetime.now(timezone.utc) + timedelta(seconds=config.cooldown_seconds)).isoformat().replace("+00:00", "Z")


def _parse_time(value: str) -> datetime:
    cleaned = value.replace("Z", "+00:00")
    parsed = datetime.fromisoformat(cleaned)
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)
