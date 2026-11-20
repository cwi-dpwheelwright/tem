"""Signed metrics envelope with replay protection."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
import base64
import json
import os

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import ed25519
from cryptography.exceptions import InvalidSignature

from .config import ConfigError, MetricsSigningConfig


@dataclass(frozen=True)
class MetricsEnvelope:
    node_id: str
    observed_utc: str
    sequence: int
    payload: dict[str, Any]
    signature: str | None
    key_id: str | None


def build_envelope(
    payload: dict[str, Any],
    *,
    node_id: str,
    state_dir: Path | None,
    config: MetricsSigningConfig,
) -> MetricsEnvelope:
    sequence = _next_sequence(state_dir)
    observed = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
    signature = None
    if config.private_key_path or config.private_key_pem_env:
        signature = _sign_payload(payload, node_id=node_id, observed_utc=observed, sequence=sequence, config=config)
    elif config.require_signature:
        raise ConfigError("metrics_signing private key required")
    return MetricsEnvelope(
        node_id=node_id,
        observed_utc=observed,
        sequence=sequence,
        payload=payload,
        signature=signature,
        key_id=config.key_id,
    )


def envelope_payload(envelope: MetricsEnvelope) -> dict[str, Any]:
    return {
        "node_id": envelope.node_id,
        "observed_utc": envelope.observed_utc,
        "sequence": envelope.sequence,
        "payload": envelope.payload,
        "signature": envelope.signature,
        "key_id": envelope.key_id,
    }


def verify_envelope(
    envelope: MetricsEnvelope,
    *,
    state_dir: Path | None,
    config: MetricsSigningConfig,
) -> None:
    if config.require_signature:
        if not envelope.signature:
            raise ConfigError("metrics signature required")
        _verify_signature(envelope, config)

    if state_dir:
        _check_replay_window(state_dir, envelope.sequence, config.replay_window)


def _sign_payload(
    payload: dict[str, Any],
    *,
    node_id: str,
    observed_utc: str,
    sequence: int,
    config: MetricsSigningConfig,
) -> str:
    signing_bytes = _signing_payload(payload, node_id=node_id, observed_utc=observed_utc, sequence=sequence)
    key = _load_private_key(config)
    signature = key.sign(signing_bytes)
    return base64.b64encode(signature).decode("utf-8")


def _verify_signature(envelope: MetricsEnvelope, config: MetricsSigningConfig) -> None:
    signing_bytes = _signing_payload(
        envelope.payload,
        node_id=envelope.node_id,
        observed_utc=envelope.observed_utc,
        sequence=envelope.sequence,
    )
    signature = _decode_signature(envelope.signature or "")
    key = _load_public_key(config)
    try:
        key.verify(signature, signing_bytes)
    except InvalidSignature as exc:
        raise ConfigError("metrics signature invalid") from exc


def _signing_payload(payload: dict[str, Any], *, node_id: str, observed_utc: str, sequence: int) -> bytes:
    body = {
        "node_id": node_id,
        "observed_utc": observed_utc,
        "sequence": sequence,
        "payload": payload,
    }
    return json.dumps(body, sort_keys=True, separators=(",", ":")).encode("utf-8")


def _decode_signature(value: str) -> bytes:
    try:
        return base64.b64decode(value, validate=True)
    except ValueError as exc:
        raise ConfigError("metrics signature must be base64") from exc


def _load_private_key(config: MetricsSigningConfig) -> ed25519.Ed25519PrivateKey:
    if config.private_key_pem_env:
        env_value = os.getenv(config.private_key_pem_env)
        if env_value:
            return _parse_private_key(env_value)
    if config.private_key_path:
        return _parse_private_key(config.private_key_path.read_text(encoding="utf-8"))
    raise ConfigError("metrics_signing private key not configured")


def _load_public_key(config: MetricsSigningConfig) -> ed25519.Ed25519PublicKey:
    if config.public_key_pem_env:
        env_value = os.getenv(config.public_key_pem_env)
        if env_value:
            return _parse_public_key(env_value)
    if config.public_key_path:
        return _parse_public_key(config.public_key_path.read_text(encoding="utf-8"))
    raise ConfigError("metrics_signing public key not configured")


def _parse_private_key(pem: str) -> ed25519.Ed25519PrivateKey:
    key = serialization.load_pem_private_key(pem.encode("utf-8"), password=None)
    if not isinstance(key, ed25519.Ed25519PrivateKey):
        raise ConfigError("metrics signing key type unsupported")
    return key


def _parse_public_key(pem: str) -> ed25519.Ed25519PublicKey:
    key = serialization.load_pem_public_key(pem.encode("utf-8"))
    if not isinstance(key, ed25519.Ed25519PublicKey):
        raise ConfigError("metrics signing key type unsupported")
    return key


def _next_sequence(state_dir: Path | None) -> int:
    if state_dir is None:
        return 1
    path = state_dir / "metrics" / "sequence.json"
    path.parent.mkdir(parents=True, exist_ok=True)
    last = 0
    if path.exists():
        raw = json.loads(path.read_text(encoding="utf-8"))
        if isinstance(raw, dict):
            last = int(raw.get("last_sequence", 0))
    next_seq = last + 1
    path.write_text(json.dumps({"last_sequence": next_seq}, indent=2, sort_keys=True), encoding="utf-8")
    return next_seq


def _check_replay_window(state_dir: Path, sequence: int, window: int) -> None:
    path = state_dir / "metrics" / "replay.json"
    path.parent.mkdir(parents=True, exist_ok=True)
    last_sequence = 0
    seen: list[int] = []
    if path.exists():
        raw = json.loads(path.read_text(encoding="utf-8"))
        if isinstance(raw, dict):
            last_sequence = int(raw.get("last_sequence", 0))
            raw_seen = raw.get("seen")
            if isinstance(raw_seen, list):
                seen = [int(item) for item in raw_seen if isinstance(item, int)]

    if sequence in seen:
        raise ConfigError("metrics sequence replay detected")
    if sequence < max(0, last_sequence - window):
        raise ConfigError("metrics sequence outside replay window")

    last_sequence = max(last_sequence, sequence)
    seen.append(sequence)
    seen = [seq for seq in seen if seq >= max(0, last_sequence - window)]
    path.write_text(
        json.dumps({"last_sequence": last_sequence, "seen": seen}, indent=2, sort_keys=True),
        encoding="utf-8",
    )
