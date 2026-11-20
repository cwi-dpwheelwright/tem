"""Persistent outbound control-channel client for home-node work."""

from __future__ import annotations

import asyncio
import base64
import hashlib
import hmac
import json
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any
from urllib import parse

import websockets

from .config import AgentConfig, ConfigError
from .enrollment import build_tls_context
from .health import health_payload
from .local_ask import LocalAskHandler, describe_local_capabilities
from .local_model import describe_local_capability_details, describe_local_model_health
from .process_miner import LocalProcessMinerHandler, describe_process_miner_capabilities
from .task_runtime import TaskRuntime


@dataclass(frozen=True)
class TokenClaims:
    work_id: str
    tenant_id: str
    user_id: str
    node_id: str
    task_type: str
    policy_scope: str | None
    expires_at_epoch: int


@dataclass(frozen=True)
class WorkExecution:
    work_id: str
    request_id: str | None
    lease_id: str
    node_id: str
    task_type: str
    payload: dict[str, Any]
    claims: TokenClaims


class TokenValidationError(ValueError):
    def __init__(self, reason_code: str) -> None:
        super().__init__(reason_code)
        self.reason_code = reason_code


class ReplayCache:
    def __init__(self, path: Path | None, max_entries: int) -> None:
        self._path = path
        self._max_entries = max_entries
        self._entries = self._load()

    def check_and_record(self, key: str, expires_at_epoch: int) -> bool:
        now = int(time.time())
        self._entries = {
            entry_key: expiry
            for entry_key, expiry in self._entries.items()
            if expiry > now
        }
        if key in self._entries:
            return False
        self._entries[key] = max(expires_at_epoch, now + 1)
        if len(self._entries) > self._max_entries:
            ordered = sorted(self._entries.items(), key=lambda item: item[1], reverse=True)
            self._entries = dict(ordered[: self._max_entries])
        self._save()
        return True

    def _load(self) -> dict[str, int]:
        if self._path is None or not self._path.exists():
            return {}
        raw = json.loads(self._path.read_text(encoding="utf-8"))
        if not isinstance(raw, dict):
            return {}
        entries = raw.get("entries")
        if not isinstance(entries, dict):
            return {}
        result: dict[str, int] = {}
        for key, value in entries.items():
            if isinstance(key, str) and isinstance(value, int):
                result[key] = value
        return result

    def _save(self) -> None:
        if self._path is None:
            return
        self._path.parent.mkdir(parents=True, exist_ok=True)
        self._path.write_text(
            json.dumps({"entries": self._entries}, indent=2, sort_keys=True),
            encoding="utf-8",
        )


def create_runtime(config: AgentConfig) -> TaskRuntime:
    runtime = TaskRuntime(config)
    runtime.register("ask.execute", LocalAskHandler(config))
    runtime.register("process_miner.observe", LocalProcessMinerHandler(config))
    return runtime


def resolve_control_url(config: AgentConfig) -> str:
    if config.control_session.connect_url:
        return config.control_session.connect_url

    parsed = parse.urlparse(config.coordinator_url)
    if parsed.scheme not in {"http", "https", "ws", "wss"}:
        raise ConfigError("control_session.connect_url required for non-http coordinator_url schemes")
    scheme = "wss" if parsed.scheme in {"https", "wss"} else "ws"
    base_path = parsed.path.rstrip("/")
    connect_path = f"{base_path}/control/nodes/connect" if base_path else "/control/nodes/connect"
    return parse.urlunparse((scheme, parsed.netloc, connect_path, "", "", ""))


def build_node_hello(config: AgentConfig) -> dict[str, Any]:
    capabilities = describe_local_capabilities(config)
    capabilities.extend(describe_process_miner_capabilities(config))
    control = config.control_session
    return {
        "type": "node_hello",
        "node_id": config.identity.node_id,
        "tenant_id": control.tenant_id or "",
        "owner_user_id": control.owner_user_id or "",
        "friendly_name": control.friendly_name or config.identity.node_id,
        "runtime_version": control.runtime_version,
        "policy_profile_version": control.policy_profile_version,
        "capabilities": capabilities,
        "capability_details": describe_local_capability_details(config),
    }


def build_keepalive(config: AgentConfig) -> dict[str, Any]:
    return {
        "type": "keepalive",
        "node_id": config.identity.node_id,
        "ts": _utc_now(),
    }


def build_heartbeat(config: AgentConfig) -> dict[str, Any]:
    health = health_payload(config)
    heartbeat = {
        "type": "heartbeat",
        "node_id": config.identity.node_id,
        "owner_user_id": config.control_session.owner_user_id,
        "status": "READY" if health["ready"] else "DEGRADED",
        "cpu_summary": {},
        "memory_summary": {},
        "runtime_version": config.control_session.runtime_version,
        "policy_profile_version": config.control_session.policy_profile_version,
        "active_session_count": 1,
        "local_privacy_mode": ["REMOTE_ALLOWED"] if config.data_boundary.allow_raw_data else ["LOCAL_ONLY"],
    }
    heartbeat.update(describe_local_model_health(config))
    return heartbeat


def process_work_envelope(
    envelope: dict[str, Any],
    config: AgentConfig,
    runtime: TaskRuntime,
    replay_cache: ReplayCache,
) -> tuple[dict[str, Any], dict[str, Any] | None]:
    ack, execution = prepare_work_envelope(envelope, config, replay_cache)
    if execution is None:
        return ack, None

    result_payload = execute_work(execution, runtime)
    return ack, result_payload


def prepare_work_envelope(
    envelope: dict[str, Any],
    config: AgentConfig,
    replay_cache: ReplayCache,
    *,
    allow_replay: bool = False,
) -> tuple[dict[str, Any], WorkExecution | None]:
    work_id = _require_message_str(envelope, "work_id")
    request_id = _read_string(envelope, "request_id")
    lease_id = _require_message_str(envelope, "lease_id")
    target_node_id = _require_message_str(envelope, "target_node_id")
    task_type = _read_string(envelope, "task_type") or "unknown"
    policy_scope = _read_string(envelope, "policy_scope")
    payload = envelope.get("payload")
    token = _read_string(envelope, "token")

    ack = {
        "type": "work_ack",
        "work_id": work_id,
        "request_id": request_id,
        "node_id": config.identity.node_id,
        "lease_id": lease_id,
        "status": "ACCEPTED",
    }

    if target_node_id != config.identity.node_id:
        ack["status"] = "REJECTED_NODE_UNREADY"
        return ack, None
    if not isinstance(payload, dict):
        ack["status"] = "REJECTED_LOCAL_POLICY"
        return ack, None
    if not token:
        ack["status"] = "REJECTED_TOKEN_INVALID"
        return ack, None

    try:
        claims = validate_work_token(
            token,
            config,
            expected_task_type=task_type,
            expected_policy_scope=policy_scope,
        )
    except TokenValidationError as exc:
        ack["status"] = exc.reason_code
        return ack, None

    if not replay_cache.check_and_record(claims.work_id, claims.expires_at_epoch) and not allow_replay:
        ack["status"] = "REJECTED_TOKEN_INVALID"
        return ack, None

    return ack, WorkExecution(
        work_id=work_id,
        request_id=request_id,
        lease_id=lease_id,
        node_id=config.identity.node_id,
        task_type=task_type,
        payload=payload,
        claims=claims,
    )


def execute_work(execution: WorkExecution, runtime: TaskRuntime) -> dict[str, Any]:
    started = time.perf_counter()
    task_result = runtime.run(execution.task_type, execution.payload)
    elapsed_ms = int((time.perf_counter() - started) * 1000)
    outcome = _map_outcome(task_result.status)

    return {
        "type": "work_result",
        "work_id": execution.work_id,
        "request_id": execution.request_id,
        "node_id": execution.node_id,
        "lease_id": execution.lease_id,
        "outcome": outcome,
        "result": task_result.details,
        "timing_ms": {"execution": elapsed_ms},
    }


def validate_work_token(
    token: str,
    config: AgentConfig,
    *,
    expected_task_type: str,
    expected_policy_scope: str | None,
) -> TokenClaims:
    secret = config.control_session.work_token_secret
    if not secret:
        raise TokenValidationError("REJECTED_TOKEN_INVALID")

    parts = token.split(".")
    if len(parts) != 3:
        raise TokenValidationError("REJECTED_TOKEN_INVALID")

    header_segment, payload_segment, signature_segment = parts
    signing_input = f"{header_segment}.{payload_segment}".encode("utf-8")
    expected_signature = _base64url_encode(
        hmac.new(secret.encode("utf-8"), signing_input, hashlib.sha256).digest()
    )
    if not hmac.compare_digest(signature_segment, expected_signature):
        raise TokenValidationError("REJECTED_TOKEN_INVALID")

    payload = _decode_segment(payload_segment)
    if payload.get("iss") != config.control_session.work_token_issuer:
        raise TokenValidationError("REJECTED_TOKEN_INVALID")
    if payload.get("aud") != config.identity.node_id or payload.get("node_id") != config.identity.node_id:
        raise TokenValidationError("REJECTED_NODE_UNREADY")

    task_type = payload.get("task_type")
    if task_type != expected_task_type:
        raise TokenValidationError("REJECTED_SCOPE_MISMATCH")

    claim_scope = payload.get("policy_scope")
    if expected_policy_scope and claim_scope and claim_scope != expected_policy_scope:
        raise TokenValidationError("REJECTED_SCOPE_MISMATCH")

    expires_at = payload.get("exp")
    if not isinstance(expires_at, int) or expires_at <= int(time.time()):
        raise TokenValidationError("REJECTED_TOKEN_INVALID")

    work_id = payload.get("jti")
    if not isinstance(work_id, str) or not work_id:
        raise TokenValidationError("REJECTED_TOKEN_INVALID")

    tenant_id = payload.get("tenant_id")
    user_id = payload.get("user_id")
    node_id = payload.get("node_id")
    if not isinstance(tenant_id, str) or not isinstance(user_id, str) or not isinstance(node_id, str):
        raise TokenValidationError("REJECTED_TOKEN_INVALID")

    return TokenClaims(
        work_id=work_id,
        tenant_id=tenant_id,
        user_id=user_id,
        node_id=node_id,
        task_type=task_type,
        policy_scope=claim_scope if isinstance(claim_scope, str) else None,
        expires_at_epoch=expires_at,
    )


async def run_control_session(config: AgentConfig, *, stop_after_first_result: bool = False) -> None:
    runtime = create_runtime(config)
    replay_cache = ReplayCache(_replay_cache_path(config), config.control_session.replay_cache_max_entries)
    await _run_control_session(config, runtime, replay_cache, stop_after_first_result=stop_after_first_result)


async def _run_control_session(
    config: AgentConfig,
    runtime: TaskRuntime,
    replay_cache: ReplayCache,
    *,
    stop_after_first_result: bool,
) -> None:
    connect_url = resolve_control_url(config)
    ssl_context = build_tls_context(config.enrollment.tls) if connect_url.startswith("wss://") else None
    reconnect_backoff = config.control_session.reconnect_backoff_seconds or (1, 2, 5, 10, 30)
    active_tasks: dict[str, asyncio.Task[None]] = {}
    active_leases: dict[str, str] = {}

    attempt = 0
    while True:
        try:
            async with websockets.connect(
                connect_url,
                additional_headers={"X-Coordination-Protocol": config.control_session.protocol_version},
                ssl=ssl_context,
                ping_interval=None,
                ping_timeout=None,
            ) as websocket:
                attempt = 0
                send_lock = asyncio.Lock()
                await _send_json(websocket, build_node_hello(config), send_lock)
                keepalive_task = asyncio.create_task(_keepalive_loop(websocket, config, send_lock))
                heartbeat_task = asyncio.create_task(_heartbeat_loop(websocket, config, send_lock))
                try:
                    async for raw_message in websocket:
                        message = json.loads(raw_message)
                        if not isinstance(message, dict):
                            continue
                        if message.get("type") != "work_envelope":
                            continue
                        work_id = _read_string(message, "work_id") or ""
                        ack, execution = prepare_work_envelope(
                            message,
                            config,
                            replay_cache,
                            allow_replay=bool(work_id and work_id in active_tasks),
                        )
                        if execution is not None:
                            active_leases[execution.work_id] = execution.lease_id

                        await _send_json(websocket, ack, send_lock)

                        if execution is not None and execution.work_id not in active_tasks:
                            task = asyncio.create_task(
                                _execute_and_send_result(
                                    websocket,
                                    runtime,
                                    execution,
                                    active_tasks,
                                    active_leases,
                                    send_lock,
                                )
                            )
                            active_tasks[execution.work_id] = task

                        if stop_after_first_result:
                            if execution is not None and execution.work_id in active_tasks:
                                await active_tasks[execution.work_id]
                            return
                finally:
                    keepalive_task.cancel()
                    heartbeat_task.cancel()
                    running_tasks = list(active_tasks.values())
                    for task in running_tasks:
                        task.cancel()
                    await asyncio.gather(keepalive_task, heartbeat_task, *running_tasks, return_exceptions=True)
                    active_tasks.clear()
                    active_leases.clear()
        except asyncio.CancelledError:
            raise
        except Exception:
            delay = reconnect_backoff[min(attempt, len(reconnect_backoff) - 1)]
            attempt += 1
            await asyncio.sleep(delay)


async def _execute_and_send_result(
    websocket: websockets.WebSocketClientProtocol,
    runtime: TaskRuntime,
    execution: WorkExecution,
    active_tasks: dict[str, asyncio.Task[None]],
    active_leases: dict[str, str],
    send_lock: asyncio.Lock,
) -> None:
    try:
        result = await asyncio.to_thread(execute_work, execution, runtime)
        result["lease_id"] = active_leases.get(execution.work_id, execution.lease_id)
        await _send_json(websocket, result, send_lock)
    finally:
        active_tasks.pop(execution.work_id, None)
        active_leases.pop(execution.work_id, None)


async def _send_json(
    websocket: websockets.WebSocketClientProtocol,
    payload: dict[str, Any],
    send_lock: asyncio.Lock,
) -> None:
    async with send_lock:
        await websocket.send(json.dumps(payload, sort_keys=True))


async def _keepalive_loop(
    websocket: websockets.WebSocketClientProtocol,
    config: AgentConfig,
    send_lock: asyncio.Lock,
) -> None:
    while True:
        await asyncio.sleep(config.control_session.keepalive_interval_seconds)
        await _send_json(websocket, build_keepalive(config), send_lock)


async def _heartbeat_loop(
    websocket: websockets.WebSocketClientProtocol,
    config: AgentConfig,
    send_lock: asyncio.Lock,
) -> None:
    while True:
        await asyncio.sleep(config.control_session.heartbeat_interval_seconds)
        await _send_json(websocket, build_heartbeat(config), send_lock)


def _decode_segment(segment: str) -> dict[str, Any]:
    padding = "=" * (-len(segment) % 4)
    data = base64.urlsafe_b64decode(segment + padding)
    payload = json.loads(data.decode("utf-8"))
    if not isinstance(payload, dict):
        raise TokenValidationError("REJECTED_TOKEN_INVALID")
    return payload


def _base64url_encode(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).decode("utf-8").rstrip("=")


def _replay_cache_path(config: AgentConfig) -> Path | None:
    if config.storage.state_dir is None:
        return None
    return config.storage.state_dir / "control_session" / "replay_cache.json"


def _read_string(mapping: dict[str, Any], key: str) -> str | None:
    value = mapping.get(key)
    return value if isinstance(value, str) and value.strip() else None


def _require_message_str(mapping: dict[str, Any], key: str) -> str:
    value = _read_string(mapping, key)
    if not value:
        raise ConfigError(f"Missing message field: {key}")
    return value


def _map_outcome(status: str) -> str:
    normalized = status.strip().lower()
    if normalized in {"ok", "completed"}:
        return "SUCCESS"
    if normalized in {"blocked", "unknown_task"}:
        return "DENIED"
    return "FAILED"


def _utc_now() -> str:
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
