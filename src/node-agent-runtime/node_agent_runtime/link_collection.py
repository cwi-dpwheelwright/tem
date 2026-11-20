"""Link metrics collection orchestration."""

from __future__ import annotations

from pathlib import Path

from .link_adapters import LinkMetricsAdapter
from .link_metrics import LinkMetricsReport, build_report


def collect_link_metrics(
    node_id: str,
    adapters: list[LinkMetricsAdapter],
    *,
    state_dir: Path | None,
) -> LinkMetricsReport:
    interfaces = []
    for adapter in adapters:
        interfaces.extend(adapter.collect(state_dir))
    return build_report(node_id, interfaces)
