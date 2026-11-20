"""Identity key handling for the node agent runtime."""

from __future__ import annotations

from dataclasses import dataclass
import os
from pathlib import Path

from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import ed25519

from .config import IdentityConfig


@dataclass(frozen=True)
class IdentityMaterial:
    node_id: str
    key_type: str
    private_key_path: Path | None
    public_key_path: Path | None
    public_key_pem: str


def ensure_identity(identity: IdentityConfig) -> IdentityMaterial:
    if identity.key_type != "ed25519":
        raise ValueError("Only ed25519 identity keys are supported")

    private_key = _load_private_key_from_env(identity.private_key_pem_env)
    if private_key is None and identity.private_key_path and identity.private_key_path.exists():
        private_key = _load_private_key(identity.private_key_path)
    if private_key is None:
        private_key = ed25519.Ed25519PrivateKey.generate()
    if identity.persist_keys and identity.private_key_path and not identity.private_key_path.exists():
        _write_private_key(identity.private_key_path, private_key)

    derived_public_key = private_key.public_key()
    derived_public_pem = _public_key_pem(derived_public_key)
    public_key_pem_env = _load_public_key_pem_env(identity.public_key_pem_env)
    if public_key_pem_env:
        _validate_public_key_matches(public_key_pem_env, derived_public_key, "env")

    public_key_pem = public_key_pem_env or derived_public_pem

    if identity.public_key_path and identity.public_key_path.exists():
        file_pem = identity.public_key_path.read_text(encoding="utf-8")
        _validate_public_key_matches(file_pem, derived_public_key, "file")
        public_key_pem = file_pem
    elif identity.persist_keys:
        _write_public_key(identity.public_key_path, derived_public_key)

    return IdentityMaterial(
        node_id=identity.node_id,
        key_type=identity.key_type,
        private_key_path=identity.private_key_path,
        public_key_path=identity.public_key_path,
        public_key_pem=public_key_pem,
    )


def _load_private_key(path: Path) -> ed25519.Ed25519PrivateKey | None:
    try:
        data = path.read_bytes()
    except OSError:
        return None
    try:
        key = serialization.load_pem_private_key(data, password=None)
    except ValueError:
        return None
    if not isinstance(key, ed25519.Ed25519PrivateKey):
        raise ValueError("Unsupported private key type")
    return key


def _write_private_key(path: Path, key: ed25519.Ed25519PrivateKey) -> None:
    if path is None:
        raise ValueError("private_key_path is required to persist keys")
    path.parent.mkdir(parents=True, exist_ok=True)
    pem = key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.PKCS8,
        encryption_algorithm=serialization.NoEncryption(),
    )
    path.write_bytes(pem)
    _tighten_permissions(path)


def _write_public_key(path: Path | None, key: ed25519.Ed25519PublicKey) -> None:
    if path is None:
        raise ValueError("public_key_path is required to persist keys")
    path.parent.mkdir(parents=True, exist_ok=True)
    pem = _public_key_pem(key).encode("utf-8")
    path.write_bytes(pem)


def _public_key_pem(key: ed25519.Ed25519PublicKey) -> str:
    pem = key.public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    )
    return pem.decode("utf-8")


def _load_private_key_from_env(env_name: str | None) -> ed25519.Ed25519PrivateKey | None:
    if not env_name:
        return None
    value = os.getenv(env_name)
    if not value:
        return None
    key = serialization.load_pem_private_key(value.encode("utf-8"), password=None)
    if not isinstance(key, ed25519.Ed25519PrivateKey):
        raise ValueError("Unsupported private key type in env")
    return key


def _load_public_key_pem_env(env_name: str | None) -> str | None:
    if not env_name:
        return None
    value = os.getenv(env_name)
    if not value:
        return None
    return value


def _validate_public_key_matches(
    pem: str, expected: ed25519.Ed25519PublicKey, source: str
) -> None:
    key = serialization.load_pem_public_key(pem.encode("utf-8"))
    if not isinstance(key, ed25519.Ed25519PublicKey):
        raise ValueError(f"Unsupported public key type in {source}")
    expected_bytes = expected.public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    )
    candidate_bytes = key.public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    )
    if expected_bytes != candidate_bytes:
        raise ValueError(f"Public key mismatch between identity and {source}")


def _tighten_permissions(path: Path) -> None:
    try:
        path.chmod(0o600)
    except OSError:
        pass
