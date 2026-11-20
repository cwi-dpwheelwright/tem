from __future__ import annotations

import os

import pytest
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric import ed25519

from node_agent_runtime.config import ConfigError, MetricsIngestionConfig, MetricsSigningConfig
from node_agent_runtime.link_adapters import ExternalLinkMetricsAdapter
from node_agent_runtime.metrics_envelope import build_envelope, envelope_payload
from node_agent_runtime.metrics_ingestion import ingest_metrics_envelope


def _keypair():
    private_key = ed25519.Ed25519PrivateKey.generate()
    public_key = private_key.public_key()
    private_pem = private_key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.PKCS8,
        encryption_algorithm=serialization.NoEncryption(),
    ).decode("utf-8")
    public_pem = public_key.public_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PublicFormat.SubjectPublicKeyInfo,
    ).decode("utf-8")
    return private_pem, public_pem


def _signing_config():
    private_pem, public_pem = _keypair()
    os.environ["METRICS_PRIV_INGEST"] = private_pem
    os.environ["METRICS_PUB_INGEST"] = public_pem
    return MetricsSigningConfig(
        require_signature=True,
        private_key_pem_env="METRICS_PRIV_INGEST",
        public_key_pem_env="METRICS_PUB_INGEST",
        key_id="key-1",
        replay_window=5,
    )


def _payload():
    return {
        "interfaces": [
            {
                "interface_id": "wifi0",
                "interface_type": "wifi",
                "observed_utc": "2026-02-01T00:00:00Z",
                "state": "up",
                "rx_mbps": 10.0,
                "tx_mbps": 5.0,
                "mtu": 1500,
                "quality": {
                    "latency_ms": 20.0,
                    "jitter_ms": 3.0,
                    "loss_ratio": 0.01,
                    "throughput_mbps": 12.0,
                    "rssi_dbm": -55.0,
                    "snr_db": 30.0,
                },
            }
        ]
    }


def test_metrics_ingestion_allows_valid_envelope(tmp_path):
    config = _signing_config()
    envelope = build_envelope(_payload(), node_id="node-1", state_dir=tmp_path, config=config)
    raw = envelope_payload(envelope)
    policy = MetricsIngestionConfig(
        allowed_node_ids=("node-1",),
        allowed_key_ids=("key-1",),
        allowed_interfaces=("wifi0",),
        allowed_interface_types=("wifi",),
    )
    payload = ingest_metrics_envelope(
        raw,
        signing=config,
        policy=policy,
        state_dir=tmp_path,
    )
    assert payload["interfaces"][0]["interface_id"] == "wifi0"


def test_metrics_ingestion_rejects_disallowed_interface(tmp_path):
    config = _signing_config()
    envelope = build_envelope(_payload(), node_id="node-1", state_dir=tmp_path, config=config)
    raw = envelope_payload(envelope)
    policy = MetricsIngestionConfig(allowed_interfaces=("cell0",))
    with pytest.raises(ConfigError):
        ingest_metrics_envelope(raw, signing=config, policy=policy, state_dir=tmp_path)


def test_metrics_ingestion_replay_detection(tmp_path):
    config = _signing_config()
    envelope = build_envelope(_payload(), node_id="node-1", state_dir=tmp_path, config=config)
    raw = envelope_payload(envelope)
    policy = MetricsIngestionConfig()
    ingest_metrics_envelope(raw, signing=config, policy=policy, state_dir=tmp_path)
    with pytest.raises(ConfigError):
        ingest_metrics_envelope(raw, signing=config, policy=policy, state_dir=tmp_path)


def test_external_link_metrics_adapter_collects(tmp_path):
    config = _signing_config()
    envelope = build_envelope(_payload(), node_id="node-1", state_dir=tmp_path, config=config)
    raw = envelope_payload(envelope)
    policy = MetricsIngestionConfig(allowed_interface_types=("wifi",))
    adapter = ExternalLinkMetricsAdapter(
        envelope_payload=raw,
        signing=config,
        ingestion=policy,
    )
    interfaces = adapter.collect(tmp_path)
    assert len(interfaces) == 1
    assert interfaces[0].interface_type == "wifi"
