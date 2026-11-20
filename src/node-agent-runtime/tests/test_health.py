from __future__ import annotations

from pathlib import Path

from node_agent_runtime.config import (
    AgentConfig,
    DegradationConfig,
    EnrollmentConfig,
    EnrollmentTlsConfig,
    IdentityConfig,
    ReadinessConfig,
    StorageConfig,
)
from node_agent_runtime.degradation import DegradationSnapshot, SystemMetrics
from node_agent_runtime import health as health_module
from node_agent_runtime.health import health_payload


def test_health_not_ready_when_keys_missing(tmp_path: Path):
    config = AgentConfig(
        coordinator_url="https://coord",
        enrollment_url="https://coord/api/membership/enroll",
        identity=IdentityConfig(
            node_id="node-01",
            private_key_path=tmp_path / "missing.key",
            public_key_path=tmp_path / "missing.pub",
            key_type="ed25519",
            persist_keys=True,
        ),
        storage=StorageConfig(state_dir=tmp_path / "state"),
        degradation=DegradationConfig(),
        enrollment=EnrollmentConfig(
            request_timeout_seconds=10,
            protocol_version="1.0",
            cert_fingerprint="aa",
            tls=EnrollmentTlsConfig(),
        ),
    )

    payload = health_payload(config)
    assert payload["ready"] is False
    assert any("missing private key" in issue for issue in payload["details"]["issues"])


def test_health_ready_when_keys_present(tmp_path: Path, monkeypatch):
    private_key = tmp_path / "id.key"
    public_key = tmp_path / "id.pub"
    private_key.write_text("key", encoding="utf-8")
    public_key.write_text("pub", encoding="utf-8")

    def fake_sample_metrics(state_dir):
        return SystemMetrics(
            observed_utc="2026-02-01T00:00:00Z",
            load_1=None,
            cpu_percent=None,
            disk_free_ratio=None,
            memory_free_ratio=None,
        )

    monkeypatch.setattr(health_module, "sample_metrics", fake_sample_metrics)

    config = AgentConfig(
        coordinator_url="https://coord",
        enrollment_url="https://coord/api/membership/enroll",
        identity=IdentityConfig(
            node_id="node-01",
            private_key_path=private_key,
            public_key_path=public_key,
            key_type="ed25519",
            persist_keys=True,
        ),
        storage=StorageConfig(state_dir=tmp_path / "state"),
        degradation=DegradationConfig(),
        enrollment=EnrollmentConfig(
            request_timeout_seconds=10,
            protocol_version="1.0",
            cert_fingerprint="aa",
            tls=EnrollmentTlsConfig(),
        ),
    )

    payload = health_payload(config)
    assert payload["ready"] is True


def test_health_not_ready_when_dependency_fails(tmp_path: Path, monkeypatch):
    private_key = tmp_path / "id.key"
    public_key = tmp_path / "id.pub"
    private_key.write_text("key", encoding="utf-8")
    public_key.write_text("pub", encoding="utf-8")

    def fake_check_dependency(url: str, timeout_seconds: int) -> bool:
        return False

    monkeypatch.setattr(health_module, "_check_dependency", fake_check_dependency)

    config = AgentConfig(
        coordinator_url="https://coord",
        enrollment_url="https://coord/api/membership/enroll",
        identity=IdentityConfig(
            node_id="node-01",
            private_key_path=private_key,
            public_key_path=public_key,
            key_type="ed25519",
            persist_keys=True,
        ),
        storage=StorageConfig(state_dir=tmp_path / "state"),
        degradation=DegradationConfig(),
        readiness=ReadinessConfig(dependency_urls=("https://coord/health",)),
        enrollment=EnrollmentConfig(
            request_timeout_seconds=10,
            protocol_version="1.0",
            cert_fingerprint="aa",
            tls=EnrollmentTlsConfig(),
        ),
    )

    payload = health_payload(config)
    assert payload["ready"] is False
    assert any("dependency unavailable" in issue for issue in payload["details"]["issues"])


def test_health_respects_readiness_threshold(tmp_path: Path, monkeypatch):
    private_key = tmp_path / "id.key"
    public_key = tmp_path / "id.pub"
    private_key.write_text("key", encoding="utf-8")
    public_key.write_text("pub", encoding="utf-8")

    def fake_sample_metrics(state_dir):
        return SystemMetrics(
            observed_utc="2026-02-01T00:00:00Z",
            load_1=None,
            cpu_percent=None,
            disk_free_ratio=None,
            memory_free_ratio=None,
        )

    def fake_evaluate(metrics, config):
        return DegradationSnapshot(
            severity=0.95,
            cpu_severity=0.0,
            disk_severity=0.0,
            memory_severity=0.0,
            details={"observed_utc": metrics.observed_utc},
        )

    monkeypatch.setattr(health_module, "sample_metrics", fake_sample_metrics)
    monkeypatch.setattr(health_module, "evaluate_degradation", fake_evaluate)

    config = AgentConfig(
        coordinator_url="https://coord",
        enrollment_url="https://coord/api/membership/enroll",
        identity=IdentityConfig(
            node_id="node-01",
            private_key_path=private_key,
            public_key_path=public_key,
            key_type="ed25519",
            persist_keys=True,
        ),
        storage=StorageConfig(state_dir=tmp_path / "state"),
        degradation=DegradationConfig(),
        readiness=ReadinessConfig(max_degradation_severity=0.9),
        enrollment=EnrollmentConfig(
            request_timeout_seconds=10,
            protocol_version="1.0",
            cert_fingerprint="aa",
            tls=EnrollmentTlsConfig(),
        ),
    )

    payload = health_payload(config)
    assert payload["ready"] is False
