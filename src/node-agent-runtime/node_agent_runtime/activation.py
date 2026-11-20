"""Activation handshake verification and storage."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from urllib import parse, request
import base64
import binascii
import json

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import ed25519
from cryptography.exceptions import InvalidSignature

from .config import ActivationConfig, ConfigError


@dataclass(frozen=True)
class ActivationHandshake:
    device_id: str
    bundle_id: str
    nonce: str
    expires_utc: str
    signature: str | None
    claims: dict[str, Any]


@dataclass(frozen=True)
class ActivationState:
    device_id: str
    bundle_id: str
    activated_utc: str
    expires_utc: str


@dataclass(frozen=True)
class ActivationStatus:
    node_id: str
    status: str
    revoked_utc: str | None
    checked_utc: str | None
    source: str | None = None


def load_handshake(path: Path) -> ActivationHandshake:
    raw = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(raw, dict):
        raise ConfigError("Activation handshake must be a JSON object")
    device_id = _require_str(raw, "device_id")
    bundle_id = _require_str(raw, "bundle_id")
    nonce = _require_str(raw, "nonce")
    expires_utc = _require_str(raw, "expires_utc")
    signature = _optional_str(raw, "signature")
    claims = raw.get("claims") if isinstance(raw.get("claims"), dict) else {}
    return ActivationHandshake(
        device_id=device_id,
        bundle_id=bundle_id,
        nonce=nonce,
        expires_utc=expires_utc,
        signature=signature,
        claims=claims,
    )


def verify_handshake(
    handshake: ActivationHandshake,
    config: ActivationConfig,
    *,
    expected_device_id: str,
) -> None:
    if handshake.device_id != expected_device_id:
        raise ConfigError("Activation handshake device_id does not match identity")

    expires = _parse_utc(handshake.expires_utc)
    if expires <= datetime.now(timezone.utc):
        raise ConfigError("Activation handshake expired")

    _validate_claims(handshake.claims, config)

    if config.require_signature:
        if not handshake.signature:
            raise ConfigError("Activation handshake signature required")
        public_key_pem = _load_public_key_pem(config)
        _verify_signature(handshake, public_key_pem)


def record_activation(state_dir: Path | None, handshake: ActivationHandshake) -> ActivationState:
    if state_dir is None:
        raise ConfigError("state_dir required to persist activation state")
    state = ActivationState(
        device_id=handshake.device_id,
        bundle_id=handshake.bundle_id,
        activated_utc=datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        expires_utc=handshake.expires_utc,
    )
    state_path = _activation_state_path(state_dir)
    state_path.parent.mkdir(parents=True, exist_ok=True)
    state_path.write_text(json.dumps(_state_payload(state), indent=2, sort_keys=True), encoding="utf-8")
    return state


def load_activation_state(state_dir: Path | None) -> ActivationState | None:
    if state_dir is None:
        return None
    state_path = _activation_state_path(state_dir)
    if not state_path.exists():
        return None
    raw = json.loads(state_path.read_text(encoding="utf-8"))
    if not isinstance(raw, dict):
        return None
    return ActivationState(
        device_id=_require_str(raw, "device_id"),
        bundle_id=_require_str(raw, "bundle_id"),
        activated_utc=_require_str(raw, "activated_utc"),
        expires_utc=_require_str(raw, "expires_utc"),
    )


def load_activation_remote_status(state_dir: Path | None) -> ActivationStatus | None:
    if state_dir is None:
        return None
    status_path = _activation_remote_status_path(state_dir)
    if not status_path.exists():
        return None
    raw = json.loads(status_path.read_text(encoding="utf-8"))
    if not isinstance(raw, dict):
        return None
    return ActivationStatus(
        node_id=_require_str(raw, "node_id"),
        status=_require_str(raw, "status"),
        revoked_utc=_optional_str(raw, "revoked_utc"),
        checked_utc=_optional_str(raw, "checked_utc"),
        source=_optional_str(raw, "source"),
    )


def is_activation_valid(state: ActivationState | None, *, expected_device_id: str) -> bool:
    if state is None:
        return False
    if state.device_id != expected_device_id:
        return False
    expires = _parse_utc(state.expires_utc)
    return expires > datetime.now(timezone.utc)


def check_and_store_nonce(
    state_dir: Path | None,
    nonce: str,
    expires_utc: str,
    *,
    max_entries: int | None = None,
) -> None:
    if state_dir is None:
        raise ConfigError("state_dir required for nonce cache")
    cache_path = _nonce_cache_path(state_dir)
    cache_path.parent.mkdir(parents=True, exist_ok=True)

    cache = _load_nonce_cache(cache_path)
    now = datetime.now(timezone.utc)
    _purge_expired(cache, now)

    if nonce in cache:
        raise ConfigError("Activation nonce replay detected")

    if max_entries is not None and max_entries > 0 and len(cache) >= max_entries:
        _evict_to_size(cache, max_entries - 1, now)

    cache[nonce] = expires_utc
    _write_nonce_cache(cache_path, cache)


def fetch_activation_status(
    config: ActivationConfig,
    *,
    coordinator_url: str,
    node_id: str,
    timeout_seconds: int | None = None,
) -> ActivationStatus:
    url = _build_status_url(config, coordinator_url, node_id)
    timeout = timeout_seconds or config.status_timeout_seconds
    req = request.Request(url, method="GET")
    with request.urlopen(req, timeout=timeout) as resp:
        body = resp.read().decode("utf-8")
    payload = json.loads(body)
    return _parse_activation_status(payload, source="remote")


def sync_activation_status(
    config: ActivationConfig,
    *,
    coordinator_url: str,
    node_id: str,
    state_dir: Path | None,
    fetch_fn=None,
) -> tuple[ActivationStatus, Path | None, bool]:
    if fetch_fn is None:
        fetch_fn = fetch_activation_status
    status = fetch_fn(
        config,
        coordinator_url=coordinator_url,
        node_id=node_id,
    )
    status_path = _activation_remote_status_path(state_dir) if state_dir else None
    if status_path:
        _save_activation_remote_status(status_path, status)

    cleared = False
    if status.status.lower() != "active" and state_dir is not None:
        state_path = _activation_state_path(state_dir)
        if state_path.exists():
            state_path.unlink()
            cleared = True
    return status, status_path, cleared


def _verify_signature(handshake: ActivationHandshake, public_key_pem: str) -> None:
    payload = _signature_payload(handshake)
    signature = _decode_signature(handshake.signature or "")
    public_key = serialization.load_pem_public_key(public_key_pem.encode("utf-8"))
    if not isinstance(public_key, ed25519.Ed25519PublicKey):
        raise ConfigError("Unsupported activation signature key type")
    try:
        public_key.verify(signature, payload)
    except InvalidSignature as exc:
        raise ConfigError("Activation handshake signature invalid") from exc


def _signature_payload(handshake: ActivationHandshake) -> bytes:
    payload = {
        "device_id": handshake.device_id,
        "bundle_id": handshake.bundle_id,
        "nonce": handshake.nonce,
        "expires_utc": handshake.expires_utc,
    }
    if handshake.claims:
        payload["claims"] = handshake.claims
    return json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")


def _decode_signature(value: str) -> bytes:
    try:
        return base64.b64decode(value, validate=True)
    except (ValueError, binascii.Error) as exc:
        raise ConfigError("Activation handshake signature must be base64") from exc


def _load_public_key_pem(config: ActivationConfig) -> str:
    if config.public_key_pem:
        return config.public_key_pem
    if config.public_key_path:
        return config.public_key_path.read_text(encoding="utf-8")
    raise ConfigError("Activation public key not configured")


def _validate_claims(claims: dict[str, Any], config: ActivationConfig) -> None:
    if config.expected_issuer:
        issuer = claims.get("issuer")
        if issuer != config.expected_issuer:
            raise ConfigError("Activation claim issuer mismatch")
    if config.expected_audience:
        audience = claims.get("audience")
        if audience != config.expected_audience:
            raise ConfigError("Activation claim audience mismatch")


def _activation_state_path(state_dir: Path) -> Path:
    return state_dir / "activation" / "state.json"


def _activation_remote_status_path(state_dir: Path) -> Path:
    return state_dir / "activation" / "remote_status.json"


def _nonce_cache_path(state_dir: Path) -> Path:
    return state_dir / "activation" / "nonce_cache.json"


def _load_nonce_cache(path: Path) -> dict[str, str]:
    if not path.exists():
        return {}
    raw = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(raw, dict):
        return {}
    return {str(k): str(v) for k, v in raw.items()}


def _write_nonce_cache(path: Path, cache: dict[str, str]) -> None:
    path.write_text(json.dumps(cache, indent=2, sort_keys=True), encoding="utf-8")


def _save_activation_remote_status(path: Path, status: ActivationStatus) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "node_id": status.node_id,
        "status": status.status,
        "revoked_utc": status.revoked_utc,
        "checked_utc": status.checked_utc,
        "source": status.source,
    }
    path.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")


def _purge_expired(cache: dict[str, str], now: datetime) -> None:
    expired = []
    for nonce, expires_utc in cache.items():
        try:
            expires = _parse_utc(expires_utc)
        except ConfigError:
            expires = now
        if expires <= now:
            expired.append(nonce)
    for nonce in expired:
        cache.pop(nonce, None)


def _evict_to_size(cache: dict[str, str], target_size: int, now: datetime) -> None:
    if target_size < 0:
        target_size = 0
    if len(cache) <= target_size:
        return
    def _expiry_for(item: tuple[str, str]) -> datetime:
        try:
            return _parse_utc(item[1])
        except ConfigError:
            return now

    items = sorted(cache.items(), key=_expiry_for)
    remove_count = max(len(cache) - target_size, 0)
    for nonce, _ in items[:remove_count]:
        cache.pop(nonce, None)


def _state_payload(state: ActivationState) -> dict[str, str]:
    return {
        "device_id": state.device_id,
        "bundle_id": state.bundle_id,
        "activated_utc": state.activated_utc,
        "expires_utc": state.expires_utc,
    }


def _parse_utc(value: str) -> datetime:
    try:
        cleaned = value.replace("Z", "+00:00")
        parsed = datetime.fromisoformat(cleaned)
    except ValueError as exc:
        raise ConfigError("Invalid expires_utc format") from exc
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def _require_str(data: dict[str, Any], key: str) -> str:
    value = data.get(key)
    if not isinstance(value, str) or not value.strip():
        raise ConfigError(f"Missing or invalid '{key}'")
    return value.strip()


def _optional_str(data: dict[str, Any], key: str) -> str | None:
    value = data.get(key)
    if value is None:
        return None
    if not isinstance(value, str) or not value.strip():
        raise ConfigError(f"Invalid '{key}'")
    return value.strip()


def _build_status_url(config: ActivationConfig, coordinator_url: str, node_id: str) -> str:
    base_url = config.status_url or f"{coordinator_url.rstrip('/')}/api/activation/status"
    parsed = parse.urlparse(base_url)
    query = dict(parse.parse_qsl(parsed.query))
    query["node_id"] = node_id
    return parse.urlunparse(parsed._replace(query=parse.urlencode(query)))


def _parse_activation_status(payload: Any, *, source: str | None) -> ActivationStatus:
    if not isinstance(payload, dict):
        raise ConfigError("Activation status response invalid")
    node_id = _require_str(payload, "node_id")
    status = _require_str(payload, "status")
    revoked_utc = _optional_str(payload, "revoked_utc")
    checked_utc = _optional_str(payload, "checked_utc")
    return ActivationStatus(
        node_id=node_id,
        status=status,
        revoked_utc=revoked_utc,
        checked_utc=checked_utc,
        source=source,
    )
