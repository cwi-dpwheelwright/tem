import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest


pytestmark = pytest.mark.simulation


def _node_agent_root() -> Path:
    return Path(__file__).resolve().parents[3] / "src" / "node-agent-runtime"


def _ensure_node_agent_imports() -> None:
    root = _node_agent_root()
    if str(root) not in sys.path:
        sys.path.insert(0, str(root))


def test_activation_revocation_blocks_execution(tmp_path: Path) -> None:
    node_root = _node_agent_root()
    if not node_root.exists():
        pytest.skip("node-agent-runtime not present")

    _ensure_node_agent_imports()

    from node_agent_runtime.activation import (  # pylint: disable=import-error
        ActivationHandshake,
        ActivationStatus,
        record_activation,
        sync_activation_status,
    )
    from node_agent_runtime.config import (  # pylint: disable=import-error
        ActivationConfig,
        AgentConfig,
        IdentityConfig,
        StorageConfig,
    )
    from node_agent_runtime.execution_guard import evaluate_execution  # pylint: disable=import-error

    state_dir = tmp_path / "state"
    state_dir.mkdir(parents=True, exist_ok=True)

    expires = (datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat().replace("+00:00", "Z")
    handshake = ActivationHandshake(
        device_id="device-01",
        bundle_id="bundle-1",
        nonce="revocation-test",
        expires_utc=expires,
        signature=None,
        claims={},
    )
    record_activation(state_dir, handshake)

    config = AgentConfig(
        coordinator_url="https://coord",
        enrollment_url="https://coord/api/membership/enroll",
        identity=IdentityConfig(
            node_id="device-01",
            private_key_path=None,
            public_key_path=None,
            key_type="ed25519",
            persist_keys=False,
        ),
        storage=StorageConfig(state_dir=state_dir),
        activation=ActivationConfig(status_url="https://coord/api/activation/status"),
    )

    def fake_fetch(cfg: ActivationConfig, *, coordinator_url: str, node_id: str) -> ActivationStatus:
        return ActivationStatus(
            node_id=node_id,
            status="revoked",
            revoked_utc="2026-02-01T00:00:00Z",
            checked_utc="2026-02-01T00:00:01Z",
            source="remote",
        )

    status, _, cleared = sync_activation_status(
        config.activation,
        coordinator_url=config.coordinator_url,
        node_id=config.identity.node_id,
        state_dir=config.storage.state_dir,
        fetch_fn=fake_fetch,
    )
    assert status.status == "revoked"
    assert cleared is True

    decision = evaluate_execution(config)
    assert decision.allowed is False
    assert "activation_invalid" in decision.reasons
