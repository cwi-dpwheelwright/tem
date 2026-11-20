"""Execution gating based on activation + kill-switch."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

from .activation import is_activation_valid, load_activation_state
from .config import AgentConfig
from .kill_switch import is_kill_switch_enabled, load_kill_switch_state, resolve_state_path


@dataclass(frozen=True)
class ExecutionDecision:
    allowed: bool
    reasons: list[str]
    activation_valid: bool
    kill_switch_enabled: bool
    details: dict[str, Any]


def evaluate_execution(config: AgentConfig) -> ExecutionDecision:
    activation_state = load_activation_state(config.storage.state_dir)
    activation_valid = is_activation_valid(activation_state, expected_device_id=config.identity.node_id)

    kill_state_path = resolve_state_path(config)
    kill_state = load_kill_switch_state(kill_state_path)
    kill_switch_enabled = is_kill_switch_enabled(kill_state)

    reasons: list[str] = []
    if not activation_valid:
        reasons.append("activation_invalid")
    if kill_switch_enabled:
        reasons.append("kill_switch_enabled")

    allowed = not reasons
    details = {
        "activation_state": activation_state.__dict__ if activation_state else None,
        "kill_switch_state": kill_state.__dict__ if kill_state else None,
    }
    return ExecutionDecision(
        allowed=allowed,
        reasons=reasons,
        activation_valid=activation_valid,
        kill_switch_enabled=kill_switch_enabled,
        details=details,
    )
