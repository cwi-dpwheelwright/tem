from __future__ import annotations

from datetime import datetime, timedelta, timezone
from pathlib import Path

from node_agent_runtime.activation import ActivationHandshake, record_activation
from node_agent_runtime.config import (
    ActivationConfig,
    AgentConfig,
    DegradationConfig,
    EnrollmentConfig,
    EnrollmentTlsConfig,
    IdentityConfig,
    KillSwitchConfig,
    StorageConfig,
)
from node_agent_runtime.execution_guard import evaluate_execution
from node_agent_runtime.kill_switch import KillSwitchState, save_kill_switch_state


def _base_config(tmp_path: Path) -> AgentConfig:
    return AgentConfig(
        coordinator_url="https://coord",
        enrollment_url="https://coord/api/membership/enroll",
        identity=IdentityConfig(
            node_id="node-01",
            private_key_path=None,
            public_key_path=None,
            key_type="ed25519",
            persist_keys=False,
        ),
        storage=StorageConfig(state_dir=tmp_path),
        degradation=DegradationConfig(),
        activation=ActivationConfig(),
        kill_switch=KillSwitchConfig(state_path=tmp_path / "kill_switch" / "state.json"),
        enrollment=EnrollmentConfig(
            request_timeout_seconds=10,
            protocol_version="1.0",
            cert_fingerprint="aa",
            tls=EnrollmentTlsConfig(),
        ),
    )


def test_execution_blocked_without_activation(tmp_path: Path):
    config = _base_config(tmp_path)
    decision = evaluate_execution(config)
    assert decision.allowed is False
    assert "activation_invalid" in decision.reasons


def test_execution_blocked_with_kill_switch(tmp_path: Path):
    config = _base_config(tmp_path)
    expires = (datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat().replace("+00:00", "Z")
    handshake = ActivationHandshake(
        device_id="node-01",
        bundle_id="bundle-1",
        nonce="abc",
        expires_utc=expires,
        signature=None,
        claims={},
    )
    record_activation(tmp_path, handshake)

    save_kill_switch_state(
        config.kill_switch.state_path,
        KillSwitchState(enabled=True, irreversible=False, updated_utc=expires, source="local"),
    )

    decision = evaluate_execution(config)
    assert decision.allowed is False
    assert "kill_switch_enabled" in decision.reasons


def test_execution_allowed_when_active_and_no_kill(tmp_path: Path):
    config = _base_config(tmp_path)
    expires = (datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat().replace("+00:00", "Z")
    handshake = ActivationHandshake(
        device_id="node-01",
        bundle_id="bundle-1",
        nonce="abc",
        expires_utc=expires,
        signature=None,
        claims={},
    )
    record_activation(tmp_path, handshake)

    decision = evaluate_execution(config)
    assert decision.allowed is True
