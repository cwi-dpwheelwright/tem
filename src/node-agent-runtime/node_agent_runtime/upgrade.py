"""Upgrade + rollback state machine."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Callable
import base64
import hashlib
import json
import os
import shutil

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import ed25519
from cryptography.exceptions import InvalidSignature

from .bundle import unseal_bundle
from .config import BundleConfig, ConfigError, UpdateConfig


@dataclass(frozen=True)
class UpdatePaths:
    base_dir: Path
    downloads_dir: Path
    staging_dir: Path
    current_dir: Path
    previous_dir: Path
    state_path: Path


@dataclass(frozen=True)
class UpdateState:
    status: str
    target_version: str | None
    current_version: str | None
    previous_version: str | None
    download_path: str | None
    staging_path: str | None
    last_error: str | None
    updated_utc: str


def resolve_update_paths(state_dir: Path | None, config: UpdateConfig) -> UpdatePaths:
    if config.updates_dir:
        base_dir = config.updates_dir
    elif state_dir:
        base_dir = state_dir / "updates"
    else:
        raise ConfigError("update.updates_dir or storage.state_dir required")
    return UpdatePaths(
        base_dir=base_dir,
        downloads_dir=base_dir / "downloads",
        staging_dir=base_dir / "staging",
        current_dir=config.current_dir or (base_dir / "current"),
        previous_dir=base_dir / "previous",
        state_path=base_dir / "state.json",
    )


def load_update_state(paths: UpdatePaths) -> UpdateState | None:
    if not paths.state_path.exists():
        return None
    raw = json.loads(paths.state_path.read_text(encoding="utf-8"))
    if not isinstance(raw, dict):
        return None
    return UpdateState(
        status=str(raw.get("status")),
        target_version=_optional_str(raw.get("target_version")),
        current_version=_optional_str(raw.get("current_version")),
        previous_version=_optional_str(raw.get("previous_version")),
        download_path=_optional_str(raw.get("download_path")),
        staging_path=_optional_str(raw.get("staging_path")),
        last_error=_optional_str(raw.get("last_error")),
        updated_utc=_optional_str(raw.get("updated_utc")) or _utc_now(),
    )


def save_update_state(paths: UpdatePaths, state: UpdateState) -> None:
    payload = {
        "status": state.status,
        "target_version": state.target_version,
        "current_version": state.current_version,
        "previous_version": state.previous_version,
        "download_path": state.download_path,
        "staging_path": state.staging_path,
        "last_error": state.last_error,
        "updated_utc": state.updated_utc,
    }
    paths.state_path.parent.mkdir(parents=True, exist_ok=True)
    paths.state_path.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")


def download_bundle(
    paths: UpdatePaths,
    *,
    target_version: str,
    download_fn: Callable[[Path, int], None],
) -> Path:
    paths.downloads_dir.mkdir(parents=True, exist_ok=True)
    final_path = paths.downloads_dir / f"{target_version}.json"
    if final_path.exists():
        return final_path
    partial_path = paths.downloads_dir / f"{target_version}.part"
    resume_from = partial_path.stat().st_size if partial_path.exists() else 0
    download_fn(partial_path, resume_from)
    partial_path.replace(final_path)
    return final_path


def stage_bundle(
    paths: UpdatePaths,
    *,
    bundle_config: BundleConfig,
    bundle_path: Path,
    target_version: str,
    update_config: UpdateConfig,
) -> Path:
    staging_dir = paths.staging_dir / target_version
    if staging_dir.exists():
        shutil.rmtree(staging_dir)
    staged_bundle = BundleConfig(
        sealed_bundle_path=bundle_path,
        unseal_dir=staging_dir,
        crypto_provider=bundle_config.crypto_provider,
        crypto_key_path=bundle_config.crypto_key_path,
        crypto_key_env=bundle_config.crypto_key_env,
    )
    unseal_bundle(staged_bundle)
    verify_update_manifest(
        staging_dir,
        update_config,
        target_version=target_version,
    )
    return staging_dir


def apply_update(
    paths: UpdatePaths,
    *,
    staging_dir: Path,
    target_version: str,
    health_check_fn: Callable[[Path], bool],
) -> UpdateState:
    paths.current_dir.parent.mkdir(parents=True, exist_ok=True)
    if paths.current_dir.exists():
        if paths.previous_dir.exists():
            shutil.rmtree(paths.previous_dir)
        paths.current_dir.replace(paths.previous_dir)

    staging_dir.replace(paths.current_dir)

    if not health_check_fn(paths.current_dir):
        if paths.previous_dir.exists():
            if paths.current_dir.exists():
                shutil.rmtree(paths.current_dir)
            paths.previous_dir.replace(paths.current_dir)
        return UpdateState(
            status="rolled_back",
            target_version=target_version,
            current_version=_read_version(paths.current_dir),
            previous_version=None,
            download_path=None,
            staging_path=str(staging_dir),
            last_error="health_check_failed",
            updated_utc=_utc_now(),
        )

    return UpdateState(
        status="active",
        target_version=target_version,
        current_version=_read_version(paths.current_dir),
        previous_version=_read_version(paths.previous_dir),
        download_path=None,
        staging_path=str(staging_dir),
        last_error=None,
        updated_utc=_utc_now(),
    )


def upgrade(
    paths: UpdatePaths,
    *,
    bundle_config: BundleConfig,
    target_version: str,
    download_fn: Callable[[Path, int], None],
    health_check_fn: Callable[[Path], bool],
    update_config: UpdateConfig | None = None,
) -> UpdateState:
    update_config = update_config or UpdateConfig()
    state = UpdateState(
        status="downloading",
        target_version=target_version,
        current_version=_read_version(paths.current_dir),
        previous_version=_read_version(paths.previous_dir),
        download_path=None,
        staging_path=None,
        last_error=None,
        updated_utc=_utc_now(),
    )
    save_update_state(paths, state)

    try:
        bundle_path = download_bundle(paths, target_version=target_version, download_fn=download_fn)
        state = _replace_state(state, status="downloaded", download_path=str(bundle_path))
        save_update_state(paths, state)
    except Exception as exc:
        state = _replace_state(state, status="download_failed", last_error=str(exc))
        save_update_state(paths, state)
        raise

    staging_dir = stage_bundle(
        paths,
        bundle_config=bundle_config,
        bundle_path=bundle_path,
        target_version=target_version,
        update_config=update_config,
    )
    state = _replace_state(state, status="staged", staging_path=str(staging_dir))
    save_update_state(paths, state)

    state = apply_update(
        paths,
        staging_dir=staging_dir,
        target_version=target_version,
        health_check_fn=health_check_fn,
    )
    save_update_state(paths, state)
    return state


def _read_version(path: Path) -> str | None:
    version_file = path / "version.txt"
    if version_file.exists():
        return version_file.read_text(encoding="utf-8").strip()
    return None


def verify_update_manifest(
    staging_dir: Path,
    config: UpdateConfig,
    *,
    target_version: str | None = None,
) -> None:
    manifest_path = staging_dir / config.manifest_filename
    if not manifest_path.exists():
        if config.require_manifest:
            raise ConfigError("update manifest missing")
        return

    manifest = _load_manifest(manifest_path)
    manifest_version = _require_str(manifest, "version")
    if target_version and manifest_version != target_version:
        raise ConfigError("update manifest version mismatch")

    files = manifest.get("files")
    if not isinstance(files, dict) or not files:
        raise ConfigError("update manifest files missing")

    for rel_path, entry in files.items():
        expected = _extract_sha256(entry, rel_path)
        resolved = _resolve_manifest_path(staging_dir, rel_path)
        actual = _sha256_file(resolved)
        if actual != expected:
            raise ConfigError(f"update manifest hash mismatch for {rel_path}")

    signature = _optional_str(manifest.get("signature"))
    if config.require_signature and not signature:
        raise ConfigError("update manifest signature required")
    if signature:
        public_key = _load_update_public_key(config)
        payload = _canonical_manifest_payload(manifest)
        try:
            public_key.verify(_decode_signature(signature), payload)
        except InvalidSignature as exc:
            raise ConfigError("update manifest signature invalid") from exc


def _load_manifest(path: Path) -> dict[str, object]:
    raw = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(raw, dict):
        raise ConfigError("update manifest must be a JSON object")
    return raw


def _canonical_manifest_payload(manifest: dict[str, object]) -> bytes:
    payload = {k: v for k, v in manifest.items() if k != "signature"}
    return json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")


def _load_update_public_key(config: UpdateConfig) -> ed25519.Ed25519PublicKey:
    pem = None
    if config.signature_public_key_pem_env:
        pem = os.getenv(config.signature_public_key_pem_env)
    if not pem and config.signature_public_key_path:
        pem = config.signature_public_key_path.read_text(encoding="utf-8")
    if not pem:
        raise ConfigError("update signature public key missing")
    key = serialization.load_pem_public_key(pem.encode("utf-8"))
    if not isinstance(key, ed25519.Ed25519PublicKey):
        raise ConfigError("update signature key must be ed25519")
    return key


def _decode_signature(value: str) -> bytes:
    try:
        return base64.b64decode(value, validate=True)
    except ValueError as exc:
        raise ConfigError("update manifest signature must be base64") from exc


def _extract_sha256(entry: object, rel_path: str) -> str:
    if isinstance(entry, str):
        expected = entry.strip().lower()
    elif isinstance(entry, dict):
        expected = _optional_str(entry.get("sha256")) or ""
    else:
        expected = ""
    if not expected or any(ch not in "0123456789abcdef" for ch in expected):
        raise ConfigError(f"update manifest sha256 invalid for {rel_path}")
    return expected


def _resolve_manifest_path(staging_dir: Path, rel_path: str) -> Path:
    if not rel_path or rel_path.startswith("/") or ".." in Path(rel_path).parts:
        raise ConfigError("update manifest path invalid")
    resolved = (staging_dir / rel_path).resolve()
    try:
        resolved.relative_to(staging_dir.resolve())
    except ValueError as exc:
        raise ConfigError("update manifest path traversal detected") from exc
    if not resolved.exists():
        raise ConfigError(f"update manifest file missing: {rel_path}")
    return resolved


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(8192), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _require_str(data: dict[str, object], key: str) -> str:
    value = data.get(key)
    if not isinstance(value, str) or not value.strip():
        raise ConfigError(f"Missing or invalid '{key}' in update manifest")
    return value.strip()


def _optional_str(value: object) -> str | None:
    if not isinstance(value, str):
        return None
    trimmed = value.strip()
    return trimmed if trimmed else None


def _replace_state(state: UpdateState, **updates: str | None) -> UpdateState:
    return UpdateState(
        status=updates.get("status") or state.status,
        target_version=updates.get("target_version") or state.target_version,
        current_version=updates.get("current_version") or state.current_version,
        previous_version=updates.get("previous_version") or state.previous_version,
        download_path=updates.get("download_path") or state.download_path,
        staging_path=updates.get("staging_path") or state.staging_path,
        last_error=updates.get("last_error") or state.last_error,
        updated_utc=_utc_now(),
    )


def _optional_str(value: object) -> str | None:
    if isinstance(value, str) and value.strip():
        return value.strip()
    return None


def _utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")
