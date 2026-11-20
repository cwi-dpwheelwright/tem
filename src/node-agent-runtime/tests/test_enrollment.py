from __future__ import annotations

from datetime import datetime, timedelta, timezone
from pathlib import Path
import time

import pytest

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.x509.oid import NameOID

from node_agent_runtime.config import (
    AgentConfig,
    ConfigError,
    ControlSessionConfig,
    DegradationConfig,
    EnrollmentAuthConfig,
    EnrollmentConfig,
    EnrollmentTlsConfig,
    IdentityConfig,
    LocalModelConfig,
    StorageConfig,
)
from node_agent_runtime.enrollment import (
    build_enrollment_payload,
    load_certificate_fingerprint,
    resolve_enrollment_auth_header,
    select_enrollment_certificate,
)
from node_agent_runtime.identity import IdentityMaterial


def _create_cert(path) -> x509.Certificate:
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    subject = issuer = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "test")])
    cert = (
        x509.CertificateBuilder()
        .subject_name(subject)
        .issuer_name(issuer)
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(datetime.now(timezone.utc) - timedelta(days=1))
        .not_valid_after(datetime.now(timezone.utc) + timedelta(days=1))
        .add_extension(x509.BasicConstraints(ca=True, path_length=None), critical=True)
        .sign(key, hashes.SHA256())
    )
    path.write_bytes(cert.public_bytes(serialization.Encoding.PEM))
    return cert


def _create_cert_and_key(path: Path, key_path: Path, *, expires_in_days: int = 1) -> x509.Certificate:
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    subject = issuer = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "test")])
    cert = (
        x509.CertificateBuilder()
        .subject_name(subject)
        .issuer_name(issuer)
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(datetime.now(timezone.utc) - timedelta(days=1))
        .not_valid_after(datetime.now(timezone.utc) + timedelta(days=expires_in_days))
        .add_extension(x509.BasicConstraints(ca=True, path_length=None), critical=True)
        .sign(key, hashes.SHA256())
    )
    path.write_bytes(cert.public_bytes(serialization.Encoding.PEM))
    key_path.write_bytes(
        key.private_bytes(
            serialization.Encoding.PEM,
            serialization.PrivateFormat.TraditionalOpenSSL,
            serialization.NoEncryption(),
        )
    )
    return cert


def test_load_certificate_fingerprint(tmp_path):
    cert_path = tmp_path / "cert.pem"
    cert = _create_cert(cert_path)
    expected = cert.fingerprint(hashes.SHA256()).hex()
    assert load_certificate_fingerprint(cert_path) == expected


def test_build_enrollment_payload_uses_node_id_and_fingerprint():
    config = AgentConfig(
        coordinator_url="https://coord",
        enrollment_url="https://coord/api/membership/enroll",
        identity=IdentityConfig(
            node_id="node-01",
            private_key_path=None,
            public_key_path=None,
            key_type="ed25519",
            persist_keys=False,
        ),
        storage=StorageConfig(state_dir=None),
        control_session=ControlSessionConfig(
            tenant_id="tenant-1",
            owner_user_id="user-1",
            runtime_version="home-node-test",
            policy_profile_version="1.0",
        ),
        degradation=DegradationConfig(),
        enrollment=EnrollmentConfig(
            request_timeout_seconds=10,
            protocol_version="1.0",
            cert_fingerprint="AA:BB:CC",
            tls=EnrollmentTlsConfig(),
        ),
    )
    identity = IdentityMaterial(
        node_id="node-01",
        key_type="ed25519",
        private_key_path=None,
        public_key_path=None,
        public_key_pem="",
    )
    payload = build_enrollment_payload(config, identity)
    assert payload == {
        "node_id": "node-01",
        "tenant_id": "tenant-1",
        "owner_user_id": "user-1",
        "device_class": "home-node",
        "capabilities": {
            "local_ask": True,
        },
        "pack_versions": {},
        "policy_profile_version": "1.0",
        "health": {
            "status": "ENROLLING",
            "runtime_version": "home-node-test",
            "policy_profile_version": "1.0",
        },
        "cert_fingerprint": "aabbcc",
        "protocol_version": "1.0",
    }


def test_build_enrollment_payload_does_not_fallback_to_kill_switch_tenant():
    config = AgentConfig(
        coordinator_url="https://coord",
        enrollment_url="https://coord/api/membership/enroll",
        identity=IdentityConfig(
            node_id="node-01",
            private_key_path=None,
            public_key_path=None,
            key_type="ed25519",
            persist_keys=False,
        ),
        storage=StorageConfig(state_dir=None),
        control_session=ControlSessionConfig(
            tenant_id=None,
            owner_user_id=None,
            runtime_version="home-node-test",
            policy_profile_version="1.0",
        ),
        degradation=DegradationConfig(),
        enrollment=EnrollmentConfig(
            request_timeout_seconds=10,
            protocol_version="1.0",
            cert_fingerprint="AA:BB:CC",
            tls=EnrollmentTlsConfig(),
        ),
    )
    identity = IdentityMaterial(
        node_id="node-01",
        key_type="ed25519",
        private_key_path=None,
        public_key_path=None,
        public_key_pem="",
    )

    payload = build_enrollment_payload(config, identity)

    assert payload["tenant_id"] == ""
    assert payload["owner_user_id"] == ""


def test_build_enrollment_payload_reports_local_model_profile():
    config = AgentConfig(
        coordinator_url="https://coord",
        enrollment_url="https://coord/api/membership/enroll",
        identity=IdentityConfig(
            node_id="node-01",
            private_key_path=None,
            public_key_path=None,
            key_type="ed25519",
            persist_keys=False,
        ),
        storage=StorageConfig(state_dir=None),
        control_session=ControlSessionConfig(
            tenant_id="tenant-1",
            owner_user_id="user-1",
            runtime_version="home-node-test",
            policy_profile_version="1.0",
        ),
        degradation=DegradationConfig(),
        enrollment=EnrollmentConfig(cert_fingerprint="AA:BB:CC", tls=EnrollmentTlsConfig()),
        local_model=LocalModelConfig(
            enabled=True,
            model="local-8b:q4",
            quantization="Q4_K_M",
            context_tokens=4096,
            max_vram_gb=6,
        ),
    )
    identity = IdentityMaterial(
        node_id="node-01",
        key_type="ed25519",
        private_key_path=None,
        public_key_path=None,
        public_key_pem="",
    )

    payload = build_enrollment_payload(config, identity)

    assert payload["capabilities"]["local_model"] is True
    assert payload["capabilities"]["local_model_profile"]["model"] == "local-8b:q4"
    assert payload["capabilities"]["local_model_profile"]["quantization"] == "Q4_K_M"
    assert payload["health"]["model_profile"]["context_tokens"] == 4096
    assert payload["health"]["hardware"]["configured_gpu_vram_gb"] == 6


def test_enrollment_auth_header_rotation():
    config = AgentConfig(
        coordinator_url="https://coord",
        enrollment_url="https://coord/api/membership/enroll",
        identity=IdentityConfig(
            node_id="node-01",
            private_key_path=None,
            public_key_path=None,
            key_type="ed25519",
            persist_keys=False,
        ),
        storage=StorageConfig(state_dir=None),
        degradation=DegradationConfig(),
        enrollment=EnrollmentConfig(
            request_timeout_seconds=10,
            protocol_version="1.0",
            cert_fingerprint="AA:BB:CC",
            tls=EnrollmentTlsConfig(),
            auth=EnrollmentAuthConfig(
                token_type="Bearer",
                token_rotation_seconds=10,
                tokens=("token-a", "token-b"),
            ),
        ),
    )

    header = resolve_enrollment_auth_header(config, now=5)
    assert header == {"Authorization": "Bearer token-a"}
    header = resolve_enrollment_auth_header(config, now=15)
    assert header == {"Authorization": "Bearer token-b"}


def test_select_enrollment_certificate_rotation_by_time(tmp_path):
    cert1_path = tmp_path / "cert1.pem"
    key1_path = tmp_path / "key1.pem"
    cert2_path = tmp_path / "cert2.pem"
    key2_path = tmp_path / "key2.pem"
    _create_cert_and_key(cert1_path, key1_path, expires_in_days=1)
    _create_cert_and_key(cert2_path, key2_path, expires_in_days=1)

    tls = EnrollmentTlsConfig(
        cert_paths=(cert1_path, cert2_path),
        key_paths=(key1_path, key2_path),
        rotation_seconds=10,
    )

    base_time = time.time()
    cycle_start = base_time - (base_time % 20)
    selected_path, _, _ = select_enrollment_certificate(tls, now=cycle_start + 5)
    assert selected_path == cert1_path
    selected_path, _, _ = select_enrollment_certificate(tls, now=cycle_start + 15)
    assert selected_path == cert2_path


def test_select_enrollment_certificate_skips_expiring(tmp_path):
    cert1_path = tmp_path / "cert1.pem"
    key1_path = tmp_path / "key1.pem"
    cert2_path = tmp_path / "cert2.pem"
    key2_path = tmp_path / "key2.pem"
    _create_cert_and_key(cert1_path, key1_path, expires_in_days=0)
    _create_cert_and_key(cert2_path, key2_path, expires_in_days=2)

    tls = EnrollmentTlsConfig(
        cert_paths=(cert1_path, cert2_path),
        key_paths=(key1_path, key2_path),
        rotation_grace_seconds=60,
    )

    selected_path, _, _ = select_enrollment_certificate(tls, now=time.time())
    assert selected_path == cert2_path


def test_select_enrollment_certificate_rejects_denylisted_serial(tmp_path):
    cert_path = tmp_path / "cert.pem"
    key_path = tmp_path / "key.pem"
    cert = _create_cert_and_key(cert_path, key_path, expires_in_days=1)
    denylist_path = tmp_path / "denylist.txt"
    denylist_path.write_text(hex(cert.serial_number), encoding="utf-8")

    tls = EnrollmentTlsConfig(
        cert_path=cert_path,
        key_path=key_path,
        revocation_denylist_path=denylist_path,
    )

    with pytest.raises(ConfigError):
        select_enrollment_certificate(tls)


def test_select_enrollment_certificate_rejects_crl_serial(tmp_path):
    issuer_key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    issuer = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "issuer")])
    cert_path = tmp_path / "cert.pem"
    key_path = tmp_path / "key.pem"
    cert_key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    cert = (
        x509.CertificateBuilder()
        .subject_name(x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "client")]))
        .issuer_name(issuer)
        .public_key(cert_key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(datetime.now(timezone.utc) - timedelta(days=1))
        .not_valid_after(datetime.now(timezone.utc) + timedelta(days=1))
        .sign(issuer_key, hashes.SHA256())
    )
    cert_path.write_bytes(cert.public_bytes(serialization.Encoding.PEM))
    key_path.write_bytes(
        cert_key.private_bytes(
            serialization.Encoding.PEM,
            serialization.PrivateFormat.TraditionalOpenSSL,
            serialization.NoEncryption(),
        )
    )

    revoked = (
        x509.RevokedCertificateBuilder()
        .serial_number(cert.serial_number)
        .revocation_date(datetime.now(timezone.utc))
        .build()
    )
    crl = (
        x509.CertificateRevocationListBuilder()
        .issuer_name(issuer)
        .last_update(datetime.now(timezone.utc))
        .next_update(datetime.now(timezone.utc) + timedelta(days=1))
        .add_revoked_certificate(revoked)
        .sign(issuer_key, hashes.SHA256())
    )
    crl_path = tmp_path / "revoked.crl"
    crl_path.write_bytes(crl.public_bytes(serialization.Encoding.PEM))

    tls = EnrollmentTlsConfig(
        cert_path=cert_path,
        key_path=key_path,
        revocation_crl_path=crl_path,
    )

    with pytest.raises(ConfigError):
        select_enrollment_certificate(tls)
