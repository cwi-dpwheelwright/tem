from __future__ import annotations

import base64
import json
from datetime import datetime, timedelta, timezone
from pathlib import Path

from cryptography.hazmat.primitives.asymmetric import ed25519
from cryptography.hazmat.primitives import serialization

from node_agent_runtime.activation import (
    ActivationHandshake,
    ActivationStatus,
    check_and_store_nonce,
    load_handshake,
    load_activation_remote_status,
    record_activation,
    sync_activation_status,
    verify_handshake,
)
from node_agent_runtime.config import ActivationConfig, AgentConfig, ConfigError, IdentityConfig, StorageConfig


def _sign(handshake: ActivationHandshake, private_key: ed25519.Ed25519PrivateKey) -> str:
    payload = {
        "device_id": handshake.device_id,
        "bundle_id": handshake.bundle_id,
        "nonce": handshake.nonce,
        "expires_utc": handshake.expires_utc,
    }
    if handshake.claims:
        payload["claims"] = handshake.claims
    data = json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")
    signature = private_key.sign(data)
    return base64.b64encode(signature).decode("utf-8")


def _activation_config(public_key_pem: str) -> ActivationConfig:
    return ActivationConfig(
        require_signature=True,
        expected_issuer="cwi-ops",
        expected_audience="cwi-node-agent-runtime",
        public_key_pem=public_key_pem,
    )


def test_verify_handshake_signature_and_claims():
    private_key = ed25519.Ed25519PrivateKey.generate()
    public_key = private_key.public_key()
    public_pem = public_key.public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    ).decode("utf-8")

    expires = (datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat().replace("+00:00", "Z")
    handshake = ActivationHandshake(
        device_id="device-01",
        bundle_id="bundle-1",
        nonce="abc",
        expires_utc=expires,
        signature=None,
        claims={"issuer": "cwi-ops", "audience": "cwi-node-agent-runtime"},
    )
    signature = _sign(handshake, private_key)
    signed_handshake = ActivationHandshake(
        device_id=handshake.device_id,
        bundle_id=handshake.bundle_id,
        nonce=handshake.nonce,
        expires_utc=handshake.expires_utc,
        signature=signature,
        claims=handshake.claims,
    )

    verify_handshake(signed_handshake, _activation_config(public_pem), expected_device_id="device-01")


def test_verify_handshake_rejects_replay(tmp_path: Path):
    expires = (datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat().replace("+00:00", "Z")
    check_and_store_nonce(tmp_path, "nonce-1", expires)
    try:
        check_and_store_nonce(tmp_path, "nonce-1", expires)
        assert False, "expected replay to raise"
    except ConfigError:
        assert True


def test_nonce_cache_eviction(tmp_path: Path):
    now = datetime.now(timezone.utc)
    expires_1 = (now + timedelta(minutes=1)).isoformat().replace("+00:00", "Z")
    expires_2 = (now + timedelta(minutes=2)).isoformat().replace("+00:00", "Z")
    expires_3 = (now + timedelta(minutes=3)).isoformat().replace("+00:00", "Z")

    check_and_store_nonce(tmp_path, "nonce-1", expires_1, max_entries=2)
    check_and_store_nonce(tmp_path, "nonce-2", expires_2, max_entries=2)
    check_and_store_nonce(tmp_path, "nonce-3", expires_3, max_entries=2)

    cache_path = tmp_path / "activation" / "nonce_cache.json"
    cache = json.loads(cache_path.read_text(encoding="utf-8"))
    assert "nonce-1" not in cache
    assert "nonce-2" in cache
    assert "nonce-3" in cache


def test_activation_state_written(tmp_path: Path):
    expires = (datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat().replace("+00:00", "Z")
    handshake = ActivationHandshake(
        device_id="device-01",
        bundle_id="bundle-1",
        nonce="abc",
        expires_utc=expires,
        signature=None,
        claims={},
    )
    state = record_activation(tmp_path, handshake)
    state_path = tmp_path / "activation" / "state.json"
    assert state_path.exists()
    loaded = json.loads(state_path.read_text(encoding="utf-8"))
    assert loaded["device_id"] == state.device_id


def test_load_handshake(tmp_path: Path):
    payload = {
        "device_id": "device-01",
        "bundle_id": "bundle-1",
        "nonce": "abc",
        "expires_utc": "2026-02-01T00:00:00Z",
        "claims": {"issuer": "cwi-ops"},
    }
    path = tmp_path / "handshake.json"
    path.write_text(json.dumps(payload), encoding="utf-8")
    handshake = load_handshake(path)
    assert handshake.device_id == "device-01"


def test_sync_activation_status_clears_state(tmp_path: Path):
    state_dir = tmp_path / "state"
    state_dir.mkdir(parents=True, exist_ok=True)
    expires = (datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat().replace("+00:00", "Z")
    handshake = ActivationHandshake(
        device_id="device-01",
        bundle_id="bundle-1",
        nonce="abc",
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

    def fake_fetch(cfg: ActivationConfig, *, coordinator_url: str, node_id: str):
        return ActivationStatus(
            node_id=node_id,
            status="revoked",
            revoked_utc="2026-02-01T00:00:00Z",
            checked_utc="2026-02-01T00:00:01Z",
            source="remote",
        )

    status, status_path, cleared = sync_activation_status(
        config.activation,
        coordinator_url=config.coordinator_url,
        node_id=config.identity.node_id,
        state_dir=config.storage.state_dir,
        fetch_fn=fake_fetch,
    )
    assert status.status == "revoked"
    assert cleared is True
    assert status_path and status_path.exists()
    assert not (state_dir / "activation" / "state.json").exists()
    loaded = load_activation_remote_status(state_dir)
    assert loaded is not None
    assert loaded.status == "revoked"


def test_sync_activation_status_keeps_state_when_active(tmp_path: Path):
    state_dir = tmp_path / "state"
    state_dir.mkdir(parents=True, exist_ok=True)
    expires = (datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat().replace("+00:00", "Z")
    handshake = ActivationHandshake(
        device_id="device-01",
        bundle_id="bundle-1",
        nonce="abc",
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

    def fake_fetch(cfg: ActivationConfig, *, coordinator_url: str, node_id: str):
        return ActivationStatus(
            node_id=node_id,
            status="active",
            revoked_utc=None,
            checked_utc="2026-02-01T00:00:01Z",
            source="remote",
        )

    _, _, cleared = sync_activation_status(
        config.activation,
        coordinator_url=config.coordinator_url,
        node_id=config.identity.node_id,
        state_dir=config.storage.state_dir,
        fetch_fn=fake_fetch,
    )
    assert cleared is False
    assert (state_dir / "activation" / "state.json").exists()
