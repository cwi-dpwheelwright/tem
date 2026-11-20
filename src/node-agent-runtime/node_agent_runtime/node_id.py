"""Helpers for deriving stable node identifiers from machine identity."""

from __future__ import annotations

import hashlib
from pathlib import Path
import string

DEFAULT_MACHINE_ID_PATH = Path("/etc/machine-id")


def derive_node_id_from_machine_id(machine_id_path: Path = DEFAULT_MACHINE_ID_PATH) -> str:
    machine_id = _load_machine_id(machine_id_path)
    digest = hashlib.sha256(machine_id.encode("ascii")).hexdigest()
    return f"home-{digest[:32]}"


def normalize_machine_id(raw_value: str) -> str:
    normalized = "".join(raw_value.split()).lower()
    if not normalized:
        raise ValueError("machine identifier is empty")
    if any(char not in string.hexdigits for char in normalized):
        raise ValueError("machine identifier must be ASCII hex")
    return normalized


def _load_machine_id(machine_id_path: Path) -> str:
    try:
        raw_value = machine_id_path.read_text(encoding="utf-8")
    except OSError as exc:
        raise ValueError(f"unable to read machine identifier from {machine_id_path}") from exc
    return normalize_machine_id(raw_value)
