"""Offline cache store with bounded eviction and integrity checks."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
import base64
import hashlib
import json
import re
import os

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import ed25519
from cryptography.exceptions import InvalidSignature

from .config import CacheConfig, ConfigError

_SAFE_ID = re.compile(r"^[a-zA-Z0-9._-]+$")


@dataclass(frozen=True)
class CacheEntry:
    asset_id: str
    filename: str
    sha256: str
    signature: str | None
    size_bytes: int
    added_utc: str
    last_access_utc: str
    path: Path


def store_cached_asset(
    config: CacheConfig,
    *,
    asset_id: str,
    filename: str,
    data: bytes,
    sha256: str | None = None,
    signature: str | None = None,
) -> CacheEntry:
    cache_dir = _require_cache_dir(config)
    _validate_asset_id(asset_id)
    _validate_filename(filename)

    digest = sha256 or _sha256(data)
    if config.require_sha256 and not sha256:
        raise ConfigError("cache sha256 required")
    if sha256 and sha256 != digest:
        raise ConfigError("cache sha256 mismatch")

    if config.require_signature and not signature:
        raise ConfigError("cache signature required")
    if signature:
        _verify_signature(config, data, signature)

    now = _utc_now()
    asset_path = cache_dir / "assets" / asset_id / filename
    asset_path.parent.mkdir(parents=True, exist_ok=True)
    asset_path.write_bytes(data)

    entry = CacheEntry(
        asset_id=asset_id,
        filename=filename,
        sha256=digest,
        signature=signature,
        size_bytes=len(data),
        added_utc=now,
        last_access_utc=now,
        path=asset_path,
    )
    index = _load_index(cache_dir)
    index["entries"][asset_id] = _entry_payload(entry)
    _apply_eviction(index, config)
    _write_index(cache_dir, index)
    return entry


def load_cached_asset(config: CacheConfig, *, asset_id: str) -> CacheEntry:
    cache_dir = _require_cache_dir(config)
    _validate_asset_id(asset_id)
    index = _load_index(cache_dir)
    entry = index["entries"].get(asset_id)
    if not entry:
        raise ConfigError("cache entry not found")
    payload = _entry_from_payload(entry, cache_dir)
    data = payload.path.read_bytes()
    digest = _sha256(data)
    if config.require_sha256 and digest != payload.sha256:
        raise ConfigError("cache sha256 verification failed")
    if payload.signature:
        _verify_signature(config, data, payload.signature)
    elif config.require_signature:
        raise ConfigError("cache signature missing")

    now = _utc_now()
    payload = CacheEntry(
        asset_id=payload.asset_id,
        filename=payload.filename,
        sha256=payload.sha256,
        signature=payload.signature,
        size_bytes=payload.size_bytes,
        added_utc=payload.added_utc,
        last_access_utc=now,
        path=payload.path,
    )
    index["entries"][asset_id] = _entry_payload(payload)
    _write_index(cache_dir, index)
    return payload


def _apply_eviction(index: dict[str, Any], config: CacheConfig) -> None:
    entries = index["entries"]
    total_bytes = sum(int(entry.get("size_bytes", 0)) for entry in entries.values())
    index["total_bytes"] = total_bytes

    if total_bytes <= config.max_bytes and len(entries) <= config.max_items:
        return

    ordered = sorted(
        entries.values(),
        key=lambda item: item.get("last_access_utc") or item.get("added_utc") or "",
    )
    while entries and (index["total_bytes"] > config.max_bytes or len(entries) > config.max_items):
        victim = ordered.pop(0)
        asset_id = victim.get("asset_id")
        if asset_id in entries:
            removed = entries.pop(asset_id)
            size = int(removed.get("size_bytes", 0))
            index["total_bytes"] = max(index["total_bytes"] - size, 0)
            _delete_asset_files(config, asset_id)


def _delete_asset_files(config: CacheConfig, asset_id: str) -> None:
    cache_dir = _require_cache_dir(config)
    asset_dir = cache_dir / "assets" / asset_id
    if not asset_dir.exists():
        return
    for path in asset_dir.rglob("*"):
        if path.is_file():
            path.unlink()
    for path in sorted(asset_dir.rglob("*"), reverse=True):
        if path.is_dir():
            path.rmdir()


def _load_index(cache_dir: Path) -> dict[str, Any]:
    index_path = cache_dir / "index.json"
    if not index_path.exists():
        return {"entries": {}, "total_bytes": 0}
    raw = json.loads(index_path.read_text(encoding="utf-8"))
    if not isinstance(raw, dict):
        return {"entries": {}, "total_bytes": 0}
    entries = raw.get("entries")
    if not isinstance(entries, dict):
        entries = {}
    total_bytes = raw.get("total_bytes")
    if not isinstance(total_bytes, int):
        total_bytes = 0
    return {"entries": entries, "total_bytes": total_bytes}


def _write_index(cache_dir: Path, index: dict[str, Any]) -> None:
    index_path = cache_dir / "index.json"
    index_path.parent.mkdir(parents=True, exist_ok=True)
    index_path.write_text(json.dumps(index, indent=2, sort_keys=True), encoding="utf-8")


def _entry_payload(entry: CacheEntry) -> dict[str, Any]:
    return {
        "asset_id": entry.asset_id,
        "filename": entry.filename,
        "sha256": entry.sha256,
        "signature": entry.signature,
        "size_bytes": entry.size_bytes,
        "added_utc": entry.added_utc,
        "last_access_utc": entry.last_access_utc,
        "path": str(entry.path),
    }


def _entry_from_payload(payload: dict[str, Any], cache_dir: Path) -> CacheEntry:
    asset_id = _require_str(payload, "asset_id")
    filename = _require_str(payload, "filename")
    sha256 = _require_str(payload, "sha256")
    signature = payload.get("signature") if isinstance(payload.get("signature"), str) else None
    size_bytes = int(payload.get("size_bytes", 0))
    added_utc = _require_str(payload, "added_utc")
    last_access_utc = _require_str(payload, "last_access_utc")
    path_str = payload.get("path")
    if not isinstance(path_str, str) or not path_str:
        path_str = str(cache_dir / "assets" / asset_id / filename)
    path = Path(path_str)
    return CacheEntry(
        asset_id=asset_id,
        filename=filename,
        sha256=sha256,
        signature=signature,
        size_bytes=size_bytes,
        added_utc=added_utc,
        last_access_utc=last_access_utc,
        path=path,
    )


def _validate_asset_id(asset_id: str) -> None:
    if not asset_id or not _SAFE_ID.match(asset_id):
        raise ConfigError("cache asset_id must be alphanumeric/._-")


def _validate_filename(filename: str) -> None:
    if not filename or "/" in filename or "\\" in filename:
        raise ConfigError("cache filename must be a simple name")


def _require_cache_dir(config: CacheConfig) -> Path:
    if config.cache_dir is None:
        raise ConfigError("cache.cache_dir required")
    return config.cache_dir


def _sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def _verify_signature(config: CacheConfig, data: bytes, signature: str) -> None:
    public_key_pem = _load_public_key_pem(config)
    signature_bytes = _decode_signature(signature)
    public_key = serialization.load_pem_public_key(public_key_pem.encode("utf-8"))
    if not isinstance(public_key, ed25519.Ed25519PublicKey):
        raise ConfigError("cache signature key type unsupported")
    try:
        public_key.verify(signature_bytes, data)
    except InvalidSignature as exc:
        raise ConfigError("cache signature invalid") from exc


def _decode_signature(value: str) -> bytes:
    try:
        return base64.b64decode(value, validate=True)
    except ValueError as exc:
        raise ConfigError("cache signature must be base64") from exc


def _load_public_key_pem(config: CacheConfig) -> str:
    if config.signature_public_key_pem_env:
        pem = os.getenv(config.signature_public_key_pem_env)
        if pem:
            return pem
    if config.signature_public_key_path:
        return config.signature_public_key_path.read_text(encoding="utf-8")
    raise ConfigError("cache signature public key not configured")


def _require_str(data: dict[str, Any], key: str) -> str:
    value = data.get(key)
    if not isinstance(value, str) or not value.strip():
        raise ConfigError(f"Missing or invalid '{key}'")
    return value.strip()
