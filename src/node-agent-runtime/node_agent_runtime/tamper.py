"""Tamper indicator checks and policy handling."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
import hashlib
import json
import os
import uuid

from .config import AgentConfig, ConfigError, TamperConfig
from .kill_switch import KillSwitchState, resolve_state_path, save_kill_switch_state


@dataclass(frozen=True)
class TamperIndicatorResult:
    indicator: str
    status: str
    detail: dict[str, Any]
    tampered: bool


@dataclass(frozen=True)
class TamperAssessment:
    observed_utc: str
    severity: float
    tampered: int
    total: int
    indicators: list[TamperIndicatorResult]


@dataclass(frozen=True)
class TamperPolicyDecision:
    action: str
    triggered: bool
    minimum_severity: float


@dataclass(frozen=True)
class TamperPolicyResult:
    action: str
    triggered: bool
    applied: bool
    error: str | None = None
    details: dict[str, Any] | None = None


@dataclass(frozen=True)
class TamperEvent:
    event_id: str
    node_id: str
    observed_utc: str
    severity: float
    tampered: int
    total: int
    indicators: list[TamperIndicatorResult]
    policy: TamperPolicyResult


def evaluate_tamper(config: TamperConfig) -> TamperAssessment:
    indicators: list[TamperIndicatorResult] = []

    for path in config.indicator_files_exist:
        indicators.append(_check_file_exists(path))

    for path, expected_hash in config.indicator_files_sha256.items():
        indicators.append(_check_file_sha256(path, expected_hash))

    for env_var in config.indicator_env_vars:
        indicators.append(_check_env_var(env_var))

    total = len(indicators)
    tampered = sum(1 for item in indicators if item.tampered)
    severity = round(tampered / total, 3) if total else 0.0
    observed = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    return TamperAssessment(
        observed_utc=observed,
        severity=severity,
        tampered=tampered,
        total=total,
        indicators=indicators,
    )


def decide_tamper_policy(assessment: TamperAssessment, config: TamperConfig) -> TamperPolicyDecision:
    triggered = assessment.severity >= config.policy_minimum_severity
    return TamperPolicyDecision(
        action=config.policy_action,
        triggered=triggered,
        minimum_severity=config.policy_minimum_severity,
    )


def apply_tamper_policy(
    decision: TamperPolicyDecision,
    *,
    config: AgentConfig,
    observed_utc: str,
    event_id: str,
) -> TamperPolicyResult:
    if not decision.triggered or decision.action == "ignore":
        return TamperPolicyResult(
            action=decision.action,
            triggered=decision.triggered,
            applied=False,
        )

    if decision.action == "ticket":
        ticket_path = _resolve_ticket_path(config)
        if ticket_path is None:
            return TamperPolicyResult(
                action=decision.action,
                triggered=decision.triggered,
                applied=False,
                error="ticket_path unavailable",
            )
        ticket_path.parent.mkdir(parents=True, exist_ok=True)
        payload = {
            "event_id": event_id,
            "observed_utc": observed_utc,
            "node_id": config.identity.node_id,
            "severity": decision.minimum_severity,
        }
        with ticket_path.open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(payload, sort_keys=True))
            handle.write("\n")
        return TamperPolicyResult(
            action=decision.action,
            triggered=decision.triggered,
            applied=True,
            details={"ticket_path": str(ticket_path)},
        )

    if decision.action in {"kill", "kill_irreversible"}:
        state_path = resolve_state_path(config)
        if state_path is None:
            return TamperPolicyResult(
                action=decision.action,
                triggered=decision.triggered,
                applied=False,
                error="kill_switch.state_path unavailable",
            )
        state = KillSwitchState(
            enabled=True,
            irreversible=decision.action == "kill_irreversible",
            updated_utc=observed_utc,
            source="tamper",
        )
        save_kill_switch_state(state_path, state)
        return TamperPolicyResult(
            action=decision.action,
            triggered=decision.triggered,
            applied=True,
            details={"kill_state_path": str(state_path)},
        )

    return TamperPolicyResult(
        action=decision.action,
        triggered=decision.triggered,
        applied=False,
        error="unknown policy action",
    )


def emit_tamper_event(
    assessment: TamperAssessment,
    *,
    node_id: str,
    state_dir: Path | None,
    policy: TamperPolicyResult,
    event_id: str | None = None,
) -> TamperEvent:
    resolved_event_id = event_id or uuid.uuid4().hex
    event = TamperEvent(
        event_id=resolved_event_id,
        node_id=node_id,
        observed_utc=assessment.observed_utc,
        severity=assessment.severity,
        tampered=assessment.tampered,
        total=assessment.total,
        indicators=assessment.indicators,
        policy=policy,
    )
    _append_event(event, state_dir)
    return event


def event_payload(event: TamperEvent) -> dict[str, Any]:
    return {
        "event_id": event.event_id,
        "node_id": event.node_id,
        "observed_utc": event.observed_utc,
        "severity": event.severity,
        "tampered": event.tampered,
        "total": event.total,
        "indicators": [
            {
                "indicator": item.indicator,
                "status": item.status,
                "detail": item.detail,
                "tampered": item.tampered,
            }
            for item in event.indicators
        ],
        "policy": {
            "action": event.policy.action,
            "triggered": event.policy.triggered,
            "applied": event.policy.applied,
            "error": event.policy.error,
            "details": event.policy.details,
        },
    }


def _check_file_exists(path: Path) -> TamperIndicatorResult:
    exists = path.exists()
    return TamperIndicatorResult(
        indicator="file_exists",
        status="ok" if exists else "missing",
        detail={"path": str(path)},
        tampered=not exists,
    )


def _check_file_sha256(path: Path, expected_hash: str) -> TamperIndicatorResult:
    if not path.exists():
        return TamperIndicatorResult(
            indicator="file_sha256",
            status="missing",
            detail={"path": str(path)},
            tampered=True,
        )
    digest = _sha256(path)
    matches = digest == expected_hash
    return TamperIndicatorResult(
        indicator="file_sha256",
        status="ok" if matches else "mismatch",
        detail={"path": str(path), "expected": expected_hash, "actual": digest},
        tampered=not matches,
    )


def _check_env_var(name: str) -> TamperIndicatorResult:
    value = os.getenv(name)
    is_set = value is not None and str(value).strip() != ""
    return TamperIndicatorResult(
        indicator="env_var",
        status="set" if is_set else "ok",
        detail={"name": name},
        tampered=is_set,
    )


def _sha256(path: Path) -> str:
    hasher = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(8192), b""):
            hasher.update(chunk)
    return hasher.hexdigest()


def _append_event(event: TamperEvent, state_dir: Path | None) -> None:
    if state_dir is None:
        return
    events_path = state_dir / "tamper" / "events.jsonl"
    events_path.parent.mkdir(parents=True, exist_ok=True)
    payload = event_payload(event)
    with events_path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(payload, sort_keys=True))
        handle.write("\n")


def _resolve_ticket_path(config: AgentConfig) -> Path | None:
    if config.tamper.policy_ticket_path:
        return config.tamper.policy_ticket_path
    if config.storage.state_dir is None:
        return None
    return config.storage.state_dir / "tamper" / "tickets.jsonl"
