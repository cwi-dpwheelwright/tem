"""Link metrics transport integration."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any
from urllib import request
import json

from .config import ConfigError, LinkMetricsTransportConfig, MetricsSigningConfig
from .link_metrics import LinkMetricsReport, report_payload
from .metrics_envelope import build_envelope, envelope_payload


@dataclass(frozen=True)
class LinkMetricsPublishResult:
    sent: bool
    envelope_used: bool


def build_link_metrics_payload(
    report: LinkMetricsReport,
    *,
    node_id: str,
    state_dir: Path | None,
    transport: LinkMetricsTransportConfig,
    signing: MetricsSigningConfig,
) -> tuple[dict[str, Any], bool]:
    payload = report_payload(report)
    use_envelope = transport.use_envelope or signing.require_signature or bool(
        signing.private_key_path or signing.private_key_pem_env
    )
    if use_envelope:
        envelope = build_envelope(
            payload,
            node_id=node_id,
            state_dir=state_dir,
            config=signing,
        )
        return envelope_payload(envelope), True
    return payload, False


def send_link_metrics_report(
    report: LinkMetricsReport,
    *,
    node_id: str,
    state_dir: Path | None,
    transport: LinkMetricsTransportConfig,
    signing: MetricsSigningConfig,
) -> LinkMetricsPublishResult:
    if not transport.transport_url:
        raise ConfigError("link_metrics.transport_url required")
    payload, envelope_used = build_link_metrics_payload(
        report,
        node_id=node_id,
        state_dir=state_dir,
        transport=transport,
        signing=signing,
    )
    data = json.dumps(payload, sort_keys=True).encode("utf-8")
    req = request.Request(
        transport.transport_url,
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with request.urlopen(req, timeout=transport.transport_timeout_seconds) as resp:
        return LinkMetricsPublishResult(sent=200 <= resp.status < 300, envelope_used=envelope_used)
