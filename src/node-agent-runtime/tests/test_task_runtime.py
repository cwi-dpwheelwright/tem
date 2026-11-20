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
    PriorityConfig,
    StorageConfig,
)
from node_agent_runtime.task_runtime import TaskResult, TaskRuntime


class DummyHandler:
    def __init__(self) -> None:
        self.called = False

    def run(self, payload: dict[str, object]) -> TaskResult:
        self.called = True
        return TaskResult(status="ok", details=payload)


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


def test_task_runtime_blocks_without_activation(tmp_path: Path):
    config = _base_config(tmp_path)
    runtime = TaskRuntime(config)
    handler = DummyHandler()
    runtime.register("noop", handler)

    result = runtime.run("noop", {"value": 1})
    assert result.status == "blocked"
    assert handler.called is False


def test_task_runtime_runs_when_active(tmp_path: Path):
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

    runtime = TaskRuntime(config)
    handler = DummyHandler()
    runtime.register("noop", handler)

    result = runtime.run("noop", {"value": 2})
    assert result.status == "ok"
    assert handler.called is True


def test_task_runtime_blocks_low_priority_in_survival_mode(tmp_path: Path):
    config = _base_config(tmp_path)
    config = AgentConfig(
        coordinator_url=config.coordinator_url,
        enrollment_url=config.enrollment_url,
        identity=config.identity,
        storage=config.storage,
        degradation=config.degradation,
        activation=config.activation,
        kill_switch=config.kill_switch,
        enrollment=config.enrollment,
        priority=PriorityConfig(survival_mode=True, min_priority_level=80),
    )
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

    runtime = TaskRuntime(config)
    handler = DummyHandler()
    runtime.register("noop", handler)

    blocked = runtime.run("noop", {"priority": "low"})
    assert blocked.status == "blocked"
    assert handler.called is False

    allowed = runtime.run("noop", {"priority": "critical"})
    assert allowed.status == "ok"
    assert handler.called is True
