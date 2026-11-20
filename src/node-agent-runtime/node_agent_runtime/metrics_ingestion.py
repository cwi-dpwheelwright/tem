"""Secure external metrics ingestion."""

from __future__ import annotations

from typing import Any

from .config import ConfigError, MetricsIngestionConfig, MetricsSigningConfig
from .link_metrics import InterfaceMetrics, RadioQualityMetrics
from .metrics_envelope import MetricsEnvelope, verify_envelope


def ingest_metrics_envelope(
    payload: dict[str, Any],
    *,
    signing: MetricsSigningConfig,
    policy: MetricsIngestionConfig,
    state_dir,
) -> dict[str, Any]:
    envelope = _parse_envelope(payload)
    verify_envelope(envelope, state_dir=state_dir, config=signing)
    _enforce_allowlist(envelope, policy)
    return envelope.payload


def parse_external_interfaces(payload: dict[str, Any]) -> list[InterfaceMetrics]:
    interfaces = payload.get("interfaces")
    if not isinstance(interfaces, list):
        raise ConfigError("metrics payload interfaces invalid")
    parsed: list[InterfaceMetrics] = []
    for item in interfaces:
        if not isinstance(item, dict):
            raise ConfigError("metrics interface entry invalid")
        interface_id = _require_str(item, "interface_id")
        interface_type = _require_str(item, "interface_type")
        observed_utc = _require_str(item, "observed_utc")
        state = _require_str(item, "state")
        rx_mbps = _optional_float(item.get("rx_mbps"), "rx_mbps")
        tx_mbps = _optional_float(item.get("tx_mbps"), "tx_mbps")
        mtu = _optional_int(item.get("mtu"), "mtu")
        quality = item.get("quality")
        if quality is None:
            quality = {}
        if not isinstance(quality, dict):
            raise ConfigError("metrics interface quality invalid")
        parsed.append(
            InterfaceMetrics(
                interface_id=interface_id,
                interface_type=interface_type,
                observed_utc=observed_utc,
                state=state,
                rx_mbps=rx_mbps,
                tx_mbps=tx_mbps,
                mtu=mtu,
                quality=RadioQualityMetrics(
                    latency_ms=_optional_float(quality.get("latency_ms"), "latency_ms"),
                    jitter_ms=_optional_float(quality.get("jitter_ms"), "jitter_ms"),
                    loss_ratio=_optional_float(quality.get("loss_ratio"), "loss_ratio"),
                    throughput_mbps=_optional_float(quality.get("throughput_mbps"), "throughput_mbps"),
                    rssi_dbm=_optional_float(quality.get("rssi_dbm"), "rssi_dbm"),
                    snr_db=_optional_float(quality.get("snr_db"), "snr_db"),
                ),
            )
        )
    return parsed


def _parse_envelope(payload: dict[str, Any]) -> MetricsEnvelope:
    if not isinstance(payload, dict):
        raise ConfigError("metrics envelope must be object")
    node_id = _require_str(payload, "node_id")
    observed_utc = _require_str(payload, "observed_utc")
    sequence = payload.get("sequence")
    if not isinstance(sequence, int) or sequence <= 0:
        raise ConfigError("metrics envelope sequence invalid")
    envelope_payload = payload.get("payload")
    if not isinstance(envelope_payload, dict):
        raise ConfigError("metrics envelope payload invalid")
    signature = payload.get("signature")
    if signature is not None and (not isinstance(signature, str) or not signature.strip()):
        raise ConfigError("metrics envelope signature invalid")
    key_id = payload.get("key_id")
    if key_id is not None and (not isinstance(key_id, str) or not key_id.strip()):
        raise ConfigError("metrics envelope key_id invalid")
    return MetricsEnvelope(
        node_id=node_id,
        observed_utc=observed_utc,
        sequence=sequence,
        payload=envelope_payload,
        signature=signature,
        key_id=key_id,
    )


def _enforce_allowlist(envelope: MetricsEnvelope, policy: MetricsIngestionConfig) -> None:
    if policy.allowed_node_ids and envelope.node_id not in set(policy.allowed_node_ids):
        raise ConfigError("metrics node_id not allowed")
    if policy.allowed_key_ids and envelope.key_id not in set(policy.allowed_key_ids):
        raise ConfigError("metrics key_id not allowed")
    interfaces = envelope.payload.get("interfaces")
    if not isinstance(interfaces, list):
        return
    allowed_ids = set(policy.allowed_interfaces)
    allowed_types = set(policy.allowed_interface_types)
    for iface in interfaces:
        if not isinstance(iface, dict):
            continue
        iface_id = iface.get("interface_id")
        iface_type = iface.get("interface_type")
        if allowed_ids and iface_id not in allowed_ids:
            raise ConfigError("metrics interface_id not allowed")
        if allowed_types and iface_type not in allowed_types:
            raise ConfigError("metrics interface_type not allowed")


def _require_str(data: dict[str, Any], key: str) -> str:
    value = data.get(key)
    if not isinstance(value, str) or not value.strip():
        raise ConfigError(f"Missing or invalid '{key}'")
    return value.strip()


def _optional_float(value: Any, key: str) -> float | None:
    if value is None:
        return None
    if isinstance(value, (int, float)):
        return float(value)
    raise ConfigError(f"Invalid '{key}'")


def _optional_int(value: Any, key: str) -> int | None:
    if value is None:
        return None
    if isinstance(value, int):
        return value
    if isinstance(value, float) and value.is_integer():
        return int(value)
    raise ConfigError(f"Invalid '{key}'")
