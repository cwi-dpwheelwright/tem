from __future__ import annotations

import os

from cryptography.hazmat.primitives.asymmetric import ed25519
from cryptography.hazmat.primitives import serialization

from node_agent_runtime.config import LinkMetricsTransportConfig, MetricsSigningConfig
from node_agent_runtime.link_metrics import LinkMetricsReport
from node_agent_runtime.link_transport import build_link_metrics_payload


def test_link_transport_uses_envelope_when_enabled(tmp_path):
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
    os.environ["LM_PRIV"] = private_pem
    os.environ["LM_PUB"] = public_pem

    transport = LinkMetricsTransportConfig(transport_url="https://example.com", use_envelope=True)
    signing = MetricsSigningConfig(
        require_signature=True,
        private_key_pem_env="LM_PRIV",
        public_key_pem_env="LM_PUB",
        replay_window=10,
    )
    report = LinkMetricsReport(node_id="node-1", observed_utc="2026-02-01T00:00:00Z", interfaces=[])
    payload, envelope_used = build_link_metrics_payload(
        report,
        node_id="node-1",
        state_dir=tmp_path,
        transport=transport,
        signing=signing,
    )
    assert envelope_used is True
    assert "payload" in payload
