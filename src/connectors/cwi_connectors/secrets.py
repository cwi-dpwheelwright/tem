from __future__ import annotations

from pathlib import Path


class SecretResolutionError(ValueError):
    pass


def resolve_secret(value: str | None) -> str | None:
    if value is None:
        return None
    value = value.strip()
    if not value:
        return value
    if value.startswith("env://"):
        key = value[len("env://") :]
        if not key:
            raise SecretResolutionError("env:// reference missing key")
        env_value = _read_env(key)
        if env_value is None:
            raise SecretResolutionError(f"Missing env var: {key}")
        return env_value
    if value.startswith("file://"):
        path = value[len("file://") :]
        if not path:
            raise SecretResolutionError("file:// reference missing path")
        return Path(path).read_text(encoding="utf-8").strip()
    if value.startswith("inline://"):
        return value[len("inline://") :]
    return value


def _read_env(key: str) -> str | None:
    import os

    return os.environ.get(key)
