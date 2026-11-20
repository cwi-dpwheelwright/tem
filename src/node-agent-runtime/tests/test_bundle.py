from __future__ import annotations

import base64
import json
from datetime import datetime, timedelta, timezone
from pathlib import Path

from cryptography.fernet import Fernet

from node_agent_runtime.activation import ActivationHandshake, record_activation
from node_agent_runtime.bundle import unseal_bundle
from node_agent_runtime.config import (
    AgentConfig,
    BundleConfig,
    IdentityConfig,
    KillSwitchConfig,
    StorageConfig,
)
from node_agent_runtime.kill_switch import KillSwitchState, save_kill_switch_state
from node_agent_runtime.task_runtime import TaskResult, TaskRuntime


def _sealed_payload(files: dict[str, bytes]) -> str:
    file_payload = {
        name: base64.b64encode(content).decode("utf-8") for name, content in files.items()
    }
    blob = json.dumps({"files": file_payload}, sort_keys=True).encode("utf-8")
    return base64.b64encode(blob).decode("utf-8")


def _write_sealed_bundle(path: Path, *, payload: str, provider: str | None = None) -> None:
    bundle = {"bundle_id": "bundle-1", "payload": payload}
    if provider:
        bundle["provider"] = provider
    path.write_text(json.dumps(bundle, indent=2, sort_keys=True), encoding="utf-8")


def test_unseal_bundle_noop(tmp_path: Path):
    sealed_path = tmp_path / "sealed.json"
    payload = _sealed_payload({"task.txt": b"hello"})
    _write_sealed_bundle(sealed_path, payload=payload)

    config = BundleConfig(
        sealed_bundle_path=sealed_path,
        unseal_dir=tmp_path / "out",
        crypto_provider="noop",
    )

    result = unseal_bundle(config)
    assert result.bundle_id == "bundle-1"
    out_file = result.files[0]
    assert out_file.read_text(encoding="utf-8") == "hello"


def test_unseal_bundle_rejects_path_traversal(tmp_path: Path):
    sealed_path = tmp_path / "sealed.json"
    payload = _sealed_payload({"../evil.txt": b"nope"})
    _write_sealed_bundle(sealed_path, payload=payload)

    config = BundleConfig(
        sealed_bundle_path=sealed_path,
        unseal_dir=tmp_path / "out",
        crypto_provider="noop",
    )

    try:
        unseal_bundle(config)
        assert False, "expected path traversal to fail"
    except Exception:
        assert True


def test_unseal_bundle_fernet(tmp_path: Path):
    sealed_path = tmp_path / "sealed.json"
    key = Fernet.generate_key()
    files_payload = _sealed_payload({"task.txt": b"secure"})
    encrypted = Fernet(key).encrypt(base64.b64decode(files_payload))
    sealed = base64.b64encode(encrypted).decode("utf-8")
    _write_sealed_bundle(sealed_path, payload=sealed, provider="fernet")

    key_path = tmp_path / "bundle.key"
    key_path.write_bytes(key)

    config = BundleConfig(
        sealed_bundle_path=sealed_path,
        unseal_dir=tmp_path / "out",
        crypto_provider="fernet",
        crypto_key_path=key_path,
    )

    result = unseal_bundle(config)
    out_file = result.files[0]
    assert out_file.read_text(encoding="utf-8") == "secure"


def test_activation_unseal_run_kill(tmp_path: Path):
    state_dir = tmp_path / "state"
    state_dir.mkdir(parents=True, exist_ok=True)
    identity = IdentityConfig(
        node_id="node-01",
        private_key_path=None,
        public_key_path=None,
        key_type="ed25519",
        persist_keys=False,
    )
    config = AgentConfig(
        coordinator_url="https://coord",
        enrollment_url="https://coord/api/membership/enroll",
        identity=identity,
        storage=StorageConfig(state_dir=state_dir),
        kill_switch=KillSwitchConfig(state_path=state_dir / "kill_switch" / "state.json"),
    )

    expires = (datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat().replace("+00:00", "Z")
    handshake = ActivationHandshake(
        device_id=identity.node_id,
        bundle_id="bundle-1",
        nonce="nonce-1",
        expires_utc=expires,
        signature=None,
        claims={},
    )
    record_activation(state_dir, handshake)

    sealed_path = tmp_path / "sealed.json"
    payload = _sealed_payload({"task.txt": b"run"})
    _write_sealed_bundle(sealed_path, payload=payload)
    bundle_config = BundleConfig(
        sealed_bundle_path=sealed_path,
        unseal_dir=tmp_path / "out",
        crypto_provider="noop",
    )
    unsealed = unseal_bundle(bundle_config)
    task_file = unsealed.files[0]

    runtime = TaskRuntime(config)

    class FileTask:
        def run(self, payload: dict[str, str]) -> TaskResult:
            content = Path(payload["path"]).read_text(encoding="utf-8")
            return TaskResult(status="ok", details={"content": content})

    runtime.register("file", FileTask())
    result = runtime.run("file", {"path": str(task_file)})
    assert result.status == "ok"

    save_kill_switch_state(
        config.kill_switch.state_path,
        KillSwitchState(
            enabled=True,
            irreversible=True,
            updated_utc=expires,
            source="test",
        ),
    )
    blocked = runtime.run("file", {"path": str(task_file)})
    assert blocked.status == "blocked"
