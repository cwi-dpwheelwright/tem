"""Encrypted bundle decrypt + unseal workflow."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any, Protocol
import base64
import json
import os

from cryptography.fernet import Fernet, InvalidToken

from .config import BundleConfig, ConfigError


@dataclass(frozen=True)
class SealedBundle:
    bundle_id: str
    payload: str
    provider: str | None = None


@dataclass(frozen=True)
class UnsealedBundle:
    bundle_id: str
    output_dir: Path
    files: tuple[Path, ...]
    provider: str


class CryptoProvider(Protocol):
    name: str

    def decrypt(self, ciphertext: bytes, key: bytes | None) -> bytes:
        ...


class NoopCryptoProvider:
    name = "noop"

    def decrypt(self, ciphertext: bytes, key: bytes | None) -> bytes:
        return ciphertext


class FernetCryptoProvider:
    name = "fernet"

    def decrypt(self, ciphertext: bytes, key: bytes | None) -> bytes:
        if not key:
            raise ConfigError("bundle.crypto_key required for fernet provider")
        try:
            fernet = Fernet(key)
            return fernet.decrypt(ciphertext)
        except (ValueError, InvalidToken) as exc:
            raise ConfigError("bundle decryption failed") from exc


_PROVIDERS: dict[str, CryptoProvider] = {
    NoopCryptoProvider.name: NoopCryptoProvider(),
    FernetCryptoProvider.name: FernetCryptoProvider(),
}


def load_sealed_bundle(path: Path) -> SealedBundle:
    raw = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(raw, dict):
        raise ConfigError("sealed bundle must be a JSON object")
    bundle_id = _require_str(raw, "bundle_id")
    payload = _require_str(raw, "payload")
    provider = raw.get("provider")
    if provider is not None and (not isinstance(provider, str) or not provider.strip()):
        raise ConfigError("Invalid bundle provider")
    return SealedBundle(
        bundle_id=bundle_id,
        payload=payload,
        provider=provider.strip().lower() if isinstance(provider, str) else None,
    )


def unseal_bundle(
    config: BundleConfig,
    *,
    bundle_path: Path | None = None,
    output_dir: Path | None = None,
) -> UnsealedBundle:
    sealed_path = bundle_path or config.sealed_bundle_path
    if sealed_path is None:
        raise ConfigError("bundle.sealed_bundle_path required")
    sealed = load_sealed_bundle(sealed_path)
    provider_name = sealed.provider or config.crypto_provider or "noop"
    provider = _resolve_provider(provider_name)
    key = _load_crypto_key(config)
    decrypted = provider.decrypt(_decode_payload(sealed.payload), key)
    payload = _parse_decrypted_payload(decrypted)

    out_dir = output_dir or config.unseal_dir
    if out_dir is None:
        raise ConfigError("bundle.unseal_dir required")
    out_dir.mkdir(parents=True, exist_ok=True)

    files = _write_files(out_dir, payload.get("files", {}))
    return UnsealedBundle(
        bundle_id=sealed.bundle_id,
        output_dir=out_dir,
        files=files,
        provider=provider.name,
    )


def _resolve_provider(name: str) -> CryptoProvider:
    key = name.strip().lower()
    provider = _PROVIDERS.get(key)
    if provider is None:
        raise ConfigError(f"Unsupported bundle crypto provider: {name}")
    return provider


def _load_crypto_key(config: BundleConfig) -> bytes | None:
    if config.crypto_key_env:
        value = os.getenv(config.crypto_key_env)
        if value:
            return value.encode("utf-8")
    if config.crypto_key_path:
        return config.crypto_key_path.read_bytes()
    return None


def _decode_payload(payload: str) -> bytes:
    try:
        return base64.b64decode(payload, validate=True)
    except ValueError as exc:
        raise ConfigError("bundle payload must be base64") from exc


def _parse_decrypted_payload(payload: bytes) -> dict[str, Any]:
    try:
        raw = json.loads(payload.decode("utf-8"))
    except (ValueError, UnicodeDecodeError) as exc:
        raise ConfigError("decrypted bundle payload invalid") from exc
    if not isinstance(raw, dict):
        raise ConfigError("decrypted bundle must be a JSON object")
    files = raw.get("files")
    if files is not None and not isinstance(files, dict):
        raise ConfigError("decrypted bundle files must be a mapping")
    return raw


def _write_files(output_dir: Path, files: dict[str, Any]) -> tuple[Path, ...]:
    written: list[Path] = []
    base = output_dir.resolve()
    for name, value in files.items():
        if not isinstance(name, str) or not name.strip():
            raise ConfigError("bundle file name invalid")
        rel = _safe_relpath(name)
        if not isinstance(value, str) or not value.strip():
            raise ConfigError("bundle file payload invalid")
        data = _decode_payload(value)
        target = (output_dir / rel).resolve()
        try:
            target.relative_to(base)
        except ValueError as exc:
            raise ConfigError("bundle path traversal detected") from exc
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        written.append(target)
    return tuple(written)


def _safe_relpath(value: str) -> Path:
    path = Path(value)
    if path.is_absolute() or ".." in path.parts:
        raise ConfigError("bundle file path must be relative")
    return path


def _require_str(data: dict[str, Any], key: str) -> str:
    value = data.get(key)
    if not isinstance(value, str) or not value.strip():
        raise ConfigError(f"Missing or invalid '{key}'")
    return value.strip()
