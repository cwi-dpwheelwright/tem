from __future__ import annotations

import asyncio
import base64
import hashlib
import hmac
import json
from datetime import datetime, timedelta, timezone
from pathlib import Path

import websockets

from node_agent_runtime.activation import ActivationHandshake, record_activation
from node_agent_runtime.config import (
    ActivationConfig,
    AgentConfig,
    ControlSessionConfig,
    DegradationConfig,
    EnrollmentConfig,
    EnrollmentTlsConfig,
    IdentityConfig,
    KillSwitchConfig,
    LocalModelConfig,
    StorageConfig,
)
from node_agent_runtime.control_channel import (
    ReplayCache,
    build_heartbeat,
    build_node_hello,
    create_runtime,
    process_work_envelope,
    run_control_session,
)


def _base_config(tmp_path: Path, *, connect_url: str | None = None) -> AgentConfig:
    return AgentConfig(
        coordinator_url="https://coord.example",
        enrollment_url="https://coord.example/api/membership/enroll",
        identity=IdentityConfig(
            node_id="node-01",
            private_key_path=None,
            public_key_path=None,
            key_type="ed25519",
            persist_keys=False,
        ),
        storage=StorageConfig(state_dir=tmp_path),
        degradation=DegradationConfig(),
        activation=ActivationConfig(require_signature=False),
        kill_switch=KillSwitchConfig(state_path=tmp_path / "kill_switch" / "state.json"),
        control_session=ControlSessionConfig(
            connect_url=connect_url,
            tenant_id="tenant-1",
            owner_user_id="user-1",
            friendly_name="node-01",
            runtime_version="2026.04.04",
            policy_profile_version="1.0",
            protocol_version="1.0",
            keepalive_interval_seconds=60,
            heartbeat_interval_seconds=60,
            work_token_issuer="cwi-orchestrator",
            work_token_secret="secret-value",
            replay_cache_max_entries=32,
        ),
        enrollment=EnrollmentConfig(
            request_timeout_seconds=10,
            protocol_version="1.0",
            cert_fingerprint="aa",
            tls=EnrollmentTlsConfig(),
        ),
    )


def _activate_node(tmp_path: Path) -> None:
    expires = (datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat().replace("+00:00", "Z")
    handshake = ActivationHandshake(
        device_id="node-01",
        bundle_id="bundle-1",
        nonce="nonce-1",
        expires_utc=expires,
        signature=None,
        claims={},
    )
    record_activation(tmp_path, handshake)


def _make_token(*, user_id: str = "user-1", node_id: str = "node-01", task_type: str = "ask.execute") -> str:
    header = {"alg": "HS256", "typ": "CWIWT"}
    payload = {
        "iss": "cwi-orchestrator",
        "sub": user_id,
        "aud": node_id,
        "jti": "work-1",
        "tenant_id": "tenant-1",
        "user_id": user_id,
        "node_id": node_id,
        "policy_scope": "LOCAL_ONLY",
        "task_type": task_type,
        "pack_name": "core-pack",
        "iat": int(datetime.now(timezone.utc).timestamp()),
        "exp": int((datetime.now(timezone.utc) + timedelta(minutes=1)).timestamp()),
    }
    signing_input = f"{_encode(header)}.{_encode(payload)}"
    signature = base64.urlsafe_b64encode(
        hmac.new(b"secret-value", signing_input.encode("utf-8"), hashlib.sha256).digest()
    ).decode("utf-8").rstrip("=")
    return f"{signing_input}.{signature}"


def _encode(value: dict[str, object]) -> str:
    return base64.urlsafe_b64encode(json.dumps(value, sort_keys=True).encode("utf-8")).decode("utf-8").rstrip("=")


def test_process_work_envelope_accepts_valid_local_ask(tmp_path: Path):
    config = _base_config(tmp_path)
    _activate_node(tmp_path)
    runtime = create_runtime(config)
    replay_cache = ReplayCache(tmp_path / "control_session" / "replay_cache.json", 32)

    ack, result = process_work_envelope(
        {
            "type": "work_envelope",
            "work_id": "work-1",
            "request_id": "req-1",
            "lease_id": "lease-1",
            "request_kind": "ask",
            "task_type": "ask.execute",
            "target_node_id": "node-01",
            "policy_scope": "LOCAL_ONLY",
            "token": _make_token(),
            "payload": {
                "question": "What is local?",
                "pack": {"hash": "pack-hash-1", "version": "1.0.0"},
            },
        },
        config,
        runtime,
        replay_cache,
    )

    assert ack["status"] == "ACCEPTED"
    assert result is not None
    assert result["outcome"] == "SUCCESS"
    assert "What is local?" in result["result"]["answer"]


def test_control_messages_include_local_model_profile(tmp_path: Path):
    base = _base_config(tmp_path)
    config = AgentConfig(
        coordinator_url=base.coordinator_url,
        enrollment_url=base.enrollment_url,
        identity=base.identity,
        storage=base.storage,
        degradation=base.degradation,
        activation=base.activation,
        kill_switch=base.kill_switch,
        control_session=base.control_session,
        enrollment=base.enrollment,
        local_model=LocalModelConfig(
            enabled=True,
            model="local-8b:q4",
            quantization="Q4_K_M",
            context_tokens=4096,
            max_vram_gb=6,
        ),
    )

    hello = build_node_hello(config)
    heartbeat = build_heartbeat(config)

    assert "local_model" in hello["capabilities"]
    assert hello["capability_details"]["local_model_profile"]["model"] == "local-8b:q4"
    assert heartbeat["model_profile"]["quantization"] == "Q4_K_M"
    assert heartbeat["hardware"]["configured_gpu_vram_gb"] == 6


def test_node_hello_allows_claimable_unowned_node(tmp_path: Path):
    base = _base_config(tmp_path)
    config = AgentConfig(
        coordinator_url=base.coordinator_url,
        enrollment_url=base.enrollment_url,
        identity=base.identity,
        storage=base.storage,
        degradation=base.degradation,
        activation=base.activation,
        kill_switch=base.kill_switch,
        control_session=ControlSessionConfig(
            connect_url=base.control_session.connect_url,
            tenant_id=None,
            owner_user_id=None,
            friendly_name="claimable-node",
            runtime_version=base.control_session.runtime_version,
            policy_profile_version=base.control_session.policy_profile_version,
            protocol_version=base.control_session.protocol_version,
            keepalive_interval_seconds=base.control_session.keepalive_interval_seconds,
            heartbeat_interval_seconds=base.control_session.heartbeat_interval_seconds,
            work_token_issuer=base.control_session.work_token_issuer,
            work_token_secret=base.control_session.work_token_secret,
            replay_cache_max_entries=base.control_session.replay_cache_max_entries,
        ),
        enrollment=base.enrollment,
    )

    hello = build_node_hello(config)

    assert hello["tenant_id"] == ""
    assert hello["owner_user_id"] == ""
    assert hello["friendly_name"] == "claimable-node"


def test_process_work_envelope_accepts_linked_user_in_same_tenant(tmp_path: Path):
    config = _base_config(tmp_path)
    _activate_node(tmp_path)
    runtime = create_runtime(config)
    replay_cache = ReplayCache(tmp_path / "control_session" / "replay_cache.json", 32)

    ack, result = process_work_envelope(
        {
            "type": "work_envelope",
            "work_id": "work-1",
            "request_id": "req-1",
            "lease_id": "lease-1",
            "request_kind": "ask",
            "task_type": "ask.execute",
            "target_node_id": "node-01",
            "policy_scope": "LOCAL_ONLY",
            "token": _make_token(user_id="user-2"),
            "payload": {"question": "Should reject"},
        },
        config,
        runtime,
        replay_cache,
    )

    assert ack["status"] == "ACCEPTED"
    assert result is not None


def test_run_control_session_round_trip(tmp_path: Path):
    async def _run() -> None:
        captured: dict[str, object] = {}

        async def handler(websocket):
            hello = json.loads(await websocket.recv())
            captured["hello"] = hello
            await websocket.send(
                json.dumps(
                    {
                        "type": "work_envelope",
                        "work_id": "work-1",
                        "request_id": "req-1",
                        "lease_id": "lease-1",
                        "request_kind": "ask",
                        "task_type": "ask.execute",
                        "target_node_id": "node-01",
                        "policy_scope": "LOCAL_ONLY",
                        "token": _make_token(),
                        "payload": {"question": "Round trip?"},
                    }
                )
            )
            captured["ack"] = json.loads(await websocket.recv())
            captured["result"] = json.loads(await websocket.recv())

        async with websockets.serve(handler, "127.0.0.1", 0) as server:
            port = server.sockets[0].getsockname()[1]
            config = _base_config(tmp_path, connect_url=f"ws://127.0.0.1:{port}/control/nodes/connect")
            _activate_node(tmp_path)
            await asyncio.wait_for(run_control_session(config, stop_after_first_result=True), timeout=5)

        assert captured["hello"]["type"] == "node_hello"
        assert captured["ack"]["status"] == "ACCEPTED"
        assert captured["result"]["outcome"] == "SUCCESS"
        assert "Round trip?" in captured["result"]["result"]["answer"]

    asyncio.run(_run())
