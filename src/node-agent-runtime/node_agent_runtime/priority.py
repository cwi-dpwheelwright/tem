"""Priority routing metadata helpers."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

from .config import PriorityConfig


_PRIORITY_LEVELS = {
    "critical": 100,
    "high": 80,
    "normal": 50,
    "low": 20,
}


@dataclass(frozen=True)
class PriorityInfo:
    level: int
    label: str


def extract_priority(payload: dict[str, Any]) -> PriorityInfo:
    value = payload.get("priority")
    if value is None:
        value = payload.get("priority_level")
    if value is None:
        value = payload.get("priority_label")
    if value is None and isinstance(payload.get("metadata"), dict):
        value = payload["metadata"].get("priority")
    return normalize_priority(value)


def normalize_priority(value: Any) -> PriorityInfo:
    if isinstance(value, (int, float)):
        level = int(max(0, min(100, value)))
        return PriorityInfo(level=level, label=_label_for(level))
    if isinstance(value, str) and value.strip():
        lowered = value.strip().lower()
        if lowered in _PRIORITY_LEVELS:
            return PriorityInfo(level=_PRIORITY_LEVELS[lowered], label=lowered)
        if lowered.isdigit():
            level = int(max(0, min(100, int(lowered))))
            return PriorityInfo(level=level, label=_label_for(level))
    return PriorityInfo(level=_PRIORITY_LEVELS["normal"], label="normal")


def build_priority_metadata(value: Any) -> dict[str, Any]:
    info = normalize_priority(value)
    return {"priority": info.label, "priority_level": info.level}


def build_priority_headers(value: Any, config: PriorityConfig) -> dict[str, str]:
    info = normalize_priority(value)
    return {
        config.header_name: info.label,
        f"{config.header_name}-Level": str(info.level),
    }


def _label_for(level: int) -> str:
    if level >= _PRIORITY_LEVELS["critical"]:
        return "critical"
    if level >= _PRIORITY_LEVELS["high"]:
        return "high"
    if level >= _PRIORITY_LEVELS["normal"]:
        return "normal"
    return "low"
