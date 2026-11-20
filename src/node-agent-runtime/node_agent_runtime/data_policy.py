"""Data boundary enforcement for task payloads."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any
from urllib import parse

from .config import DataBoundaryConfig


@dataclass(frozen=True)
class DataBoundaryDecision:
    allowed: bool
    reasons: list[str]
    details: dict[str, Any]


def evaluate_data_boundary(payload: dict[str, Any], config: DataBoundaryConfig) -> DataBoundaryDecision:
    reasons: list[str] = []
    details: dict[str, Any] = {}

    raw_flag = payload.get("raw_data")
    classification = payload.get("data_classification")
    if (raw_flag is True or classification == "raw") and not config.allow_raw_data:
        reasons.append("raw_data_blocked")

    export_paths = payload.get("export_paths")
    if export_paths is not None:
        if not _paths_allowed(export_paths, config.allowed_export_paths):
            reasons.append("export_path_blocked")
            details["export_paths"] = export_paths

    export_urls = payload.get("export_urls")
    if export_urls is not None:
        if not _hosts_allowed(export_urls, config.allowed_export_hosts):
            reasons.append("export_host_blocked")
            details["export_urls"] = export_urls

    allowed = not reasons
    return DataBoundaryDecision(allowed=allowed, reasons=reasons, details=details)


def _paths_allowed(values: Any, allowed_paths: tuple[Path, ...]) -> bool:
    if not allowed_paths:
        return False
    if isinstance(values, str):
        values = [values]
    if not isinstance(values, list):
        return False
    for item in values:
        if not isinstance(item, str) or not item.strip():
            return False
        path = Path(item).expanduser().resolve()
        if not any(_is_within(path, allowed) for allowed in allowed_paths):
            return False
    return True


def _hosts_allowed(values: Any, allowed_hosts: tuple[str, ...]) -> bool:
    if not allowed_hosts:
        return False
    if isinstance(values, str):
        values = [values]
    if not isinstance(values, list):
        return False
    allowed = {host.lower() for host in allowed_hosts}
    for item in values:
        if not isinstance(item, str) or not item.strip():
            return False
        parsed = parse.urlparse(item)
        host = (parsed.hostname or "").lower()
        if host not in allowed:
            return False
    return True


def _is_within(path: Path, base: Path) -> bool:
    try:
        path.relative_to(base.resolve())
        return True
    except ValueError:
        return False
