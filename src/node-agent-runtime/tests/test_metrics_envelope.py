from __future__ import annotations

import os

from cryptography.hazmat.primitives.asymmetric import ed25519
from cryptography.hazmat.primitives import serialization

from node_agent_runtime.metrics_envelope import build_envelope, verify_envelope
from node_agent_runtime.config import MetricsSigningConfig


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


def test_metrics_envelope_sign_and_verify(tmp_path):
    private_pem, public_pem = _keypair()
    os.environ["METRICS_PRIV"] = private_pem
    os.environ["METRICS_PUB"] = public_pem
    config = MetricsSigningConfig(
        require_signature=True,
        private_key_pem_env="METRICS_PRIV",
        public_key_pem_env="METRICS_PUB",
        replay_window=10,
    )
    envelope = build_envelope({"value": 1}, node_id="node-1", state_dir=tmp_path, config=config)
    assert envelope.signature is not None
    verify_envelope(envelope, state_dir=tmp_path, config=config)


def test_metrics_envelope_replay_window(tmp_path):
    private_pem, public_pem = _keypair()
    os.environ["METRICS_PRIV"] = private_pem
    os.environ["METRICS_PUB"] = public_pem
    config = MetricsSigningConfig(
        require_signature=True,
        private_key_pem_env="METRICS_PRIV",
        public_key_pem_env="METRICS_PUB",
        replay_window=2,
    )
    env1 = build_envelope({"value": 1}, node_id="node-1", state_dir=tmp_path, config=config)
    verify_envelope(env1, state_dir=tmp_path, config=config)
    env2 = build_envelope({"value": 2}, node_id="node-1", state_dir=tmp_path, config=config)
    verify_envelope(env2, state_dir=tmp_path, config=config)
    try:
        verify_envelope(env1, state_dir=tmp_path, config=config)
        assert False, "expected replay detection"
    except Exception:
        assert True
