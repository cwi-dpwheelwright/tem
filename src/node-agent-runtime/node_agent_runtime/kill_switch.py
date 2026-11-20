"""Kill-switch propagation and local state."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any
from urllib import parse, request
import json
import os
import time

from .config import AgentConfig, ConfigError, KillSwitchConfig


@dataclass(frozen=True)
class KillSwitchState:
    enabled: bool
    irreversible: bool
    updated_utc: str | None
    source: str | None = None


def resolve_state_path(config: AgentConfig) -> Path | None:
    if config.kill_switch.state_path:
        return config.kill_switch.state_path
    if config.storage.state_dir is None:
        return None
    return config.storage.state_dir / "kill_switch" / "state.json"


def load_kill_switch_state(state_path: Path | None) -> KillSwitchState | None:
    if state_path is None or not state_path.exists():
        return None
    raw = json.loads(state_path.read_text(encoding="utf-8"))
    if not isinstance(raw, dict):
        return None
    enabled = bool(raw.get("enabled"))
    irreversible = bool(raw.get("irreversible"))
    updated_utc = raw.get("updated_utc") if isinstance(raw.get("updated_utc"), str) else None
    source = raw.get("source") if isinstance(raw.get("source"), str) else None
    return KillSwitchState(
        enabled=enabled,
        irreversible=irreversible,
        updated_utc=updated_utc,
        source=source,
    )


def save_kill_switch_state(state_path: Path | None, state: KillSwitchState) -> None:
    if state_path is None:
        raise ConfigError("state_path required to persist kill-switch state")
    state_path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "enabled": state.enabled,
        "irreversible": state.irreversible,
        "updated_utc": state.updated_utc,
        "source": state.source,
    }
    state_path.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")


def sync_kill_switch_state(
    config: AgentConfig,
    *,
    fetch_fn=None,
) -> tuple[KillSwitchState, Path | None]:
    if fetch_fn is None:
        fetch_fn = fetch_kill_switch_state
    fetched = fetch_fn(config.kill_switch)
    state_path = resolve_state_path(config)
    if state_path:
        current = load_kill_switch_state(state_path)
        merged = _merge_states(current, fetched)
        save_kill_switch_state(state_path, merged)
        return merged, state_path
    return fetched, state_path


def poll_kill_switch_state(
    config: AgentConfig,
    *,
    interval_seconds: int,
    max_iterations: int | None = None,
    sleep_fn=time.sleep,
    fetch_fn=None,
) -> None:
    if interval_seconds <= 0:
        raise ConfigError("interval_seconds must be positive")
    iterations = 0
    while True:
        sync_kill_switch_state(config, fetch_fn=fetch_fn)
        iterations += 1
        if max_iterations is not None and iterations >= max_iterations:
            return
        sleep_fn(interval_seconds)


def fetch_kill_switch_state(config: KillSwitchConfig) -> KillSwitchState:
    if not config.admin_url:
        raise ConfigError("kill_switch.admin_url required")
    url = _build_admin_url(config.admin_url, config.tenant_id)
    headers = {}
    token = _resolve_break_glass_token(config)
    if token:
        headers["X-Break-Glass-Token"] = token
    attempts = max(config.retry_attempts, 1)
    backoff = max(config.retry_backoff_seconds, 0)

    last_exc: Exception | None = None
    for attempt in range(attempts):
        try:
            req = request.Request(url, headers=headers, method="GET")
            with request.urlopen(req, timeout=10) as resp:
                body = resp.read().decode("utf-8")
            payload = json.loads(body)
            state = _parse_state(payload, source="remote")
            return _normalize_state(state)
        except ConfigError:
            raise
        except Exception as exc:  # pragma: no cover - explicit guard for transport errors
            last_exc = exc
            if attempt < attempts - 1 and backoff > 0:
                time.sleep(backoff * (2**attempt))
            continue

    raise ConfigError("Kill-switch fetch failed") from last_exc


def is_kill_switch_enabled(state: KillSwitchState | None) -> bool:
    return bool(state and state.enabled)


def _parse_state(payload: Any, *, source: str | None) -> KillSwitchState:
    if not isinstance(payload, dict):
        raise ConfigError("Kill-switch response invalid")
    enabled = bool(payload.get("enabled"))
    irreversible = bool(payload.get("irreversible"))
    updated_utc = payload.get("updated_utc") if isinstance(payload.get("updated_utc"), str) else None
    return KillSwitchState(enabled=enabled, irreversible=irreversible, updated_utc=updated_utc, source=source)


def _build_admin_url(base_url: str, tenant_id: str | None) -> str:
    if not tenant_id:
        return base_url
    parsed = parse.urlparse(base_url)
    query = dict(parse.parse_qsl(parsed.query))
    query["tenant_id"] = tenant_id
    return parse.urlunparse(parsed._replace(query=parse.urlencode(query)))


def _normalize_state(state: KillSwitchState) -> KillSwitchState:
    if state.irreversible and not state.enabled:
        return KillSwitchState(
            enabled=True,
            irreversible=True,
            updated_utc=state.updated_utc,
            source=state.source,
        )
    return state


def _merge_states(current: KillSwitchState | None, incoming: KillSwitchState) -> KillSwitchState:
    incoming = _normalize_state(incoming)
    if current is None:
        return incoming
    current = _normalize_state(current)
    if current.irreversible and current.enabled:
        if incoming.enabled:
            return KillSwitchState(
                enabled=True,
                irreversible=True,
                updated_utc=incoming.updated_utc or current.updated_utc,
                source=incoming.source or current.source,
            )
        return current
    if incoming.irreversible and incoming.enabled:
        return incoming
    return incoming


def _resolve_break_glass_token(config: KillSwitchConfig) -> str | None:
    if not config.break_glass_token_env:
        return None
    return os.getenv(config.break_glass_token_env)
