from __future__ import annotations

from pathlib import Path

import pytest

from node_agent_runtime.config import (
    AgentConfig,
    EnrollmentConfig,
    EnrollmentTlsConfig,
    IdentityConfig,
    KillSwitchConfig,
    QrServiceConfig,
    StorageConfig,
    load_config,
)
from node_agent_runtime.qr_service import (
    build_qr_payload,
    is_allowed_client,
    render_qr_svg,
)


def test_build_qr_payload_omits_tenant_and_keeps_immutable_identifiers() -> None:
    config = AgentConfig(
        coordinator_url="https://coord",
        enrollment_url="https://coord/api/membership/enroll",
        identity=IdentityConfig(
            node_id="home-032c311b95ac9a14a81f0b3fa2520ae9",
            private_key_path=None,
            public_key_path=None,
            persist_keys=False,
        ),
        storage=StorageConfig(),
        kill_switch=KillSwitchConfig(tenant_id="tenant-home-01"),
        qr_service=QrServiceConfig(),
        enrollment=EnrollmentConfig(tls=EnrollmentTlsConfig()),
    )

    payload = build_qr_payload(config)

    assert payload == {
        "identifier_version": "machine-id-sha256-v1",
        "node_id": "home-032c311b95ac9a14a81f0b3fa2520ae9",
    }


@pytest.mark.parametrize(
    ("client_ip", "expected"),
    [
        ("127.0.0.1", True),
        ("100.122.244.48", True),
        ("fd7a:115c:a1e0::12", True),
        ("203.0.113.9", False),
        (None, False),
    ],
)
def test_is_allowed_client_respects_cidr_allowlist(client_ip: str | None, expected: bool) -> None:
    allowed_networks = (
        "127.0.0.1/32",
        "100.64.0.0/10",
        "fd7a:115c:a1e0::/48",
    )
    assert is_allowed_client(client_ip, allowed_networks) is expected


def test_render_qr_svg_returns_svg_document() -> None:
    svg = render_qr_svg(
        {
            "node_id": "home-032c311b95ac9a14a81f0b3fa2520ae9",
            "identifier_version": "machine-id-sha256-v1",
        }
    )

    assert svg.lstrip().startswith("<?xml")
    assert "<svg" in svg


def test_load_config_reads_qr_service_settings(tmp_path: Path) -> None:
    config_path = tmp_path / "node-agent.yaml"
    config_path.write_text(
        """
coordinator_url: https://coord
enrollment_url: https://coord/api/membership/enroll
identity:
  node_id: home-032c311b95ac9a14a81f0b3fa2520ae9
  persist_keys: false
qr_service:
  identifier_version: machine-id-sha256-v1
  allowed_networks:
    - 127.0.0.1/32
    - 100.64.0.0/10
""".strip(),
        encoding="utf-8",
    )

    config = load_config(config_path)

    assert config.qr_service == QrServiceConfig(
        identifier_version="machine-id-sha256-v1",
        allowed_networks=("127.0.0.1/32", "100.64.0.0/10"),
    )


def test_load_config_reads_control_session_identity_from_env(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("CWI_HOME_NODE_TENANT_ID", "tenant-home-01")
    monkeypatch.setenv("CWI_HOME_NODE_OWNER_USER_ID", "user-home-01")
    monkeypatch.setenv("CWI_HOME_NODE_FRIENDLY_NAME", "home-node-07")
    config_path = tmp_path / "node-agent.yaml"
    config_path.write_text(
        """
coordinator_url: https://coord
enrollment_url: https://coord/api/membership/enroll
identity:
  node_id: home-032c311b95ac9a14a81f0b3fa2520ae9
  persist_keys: false
control_session:
  tenant_id_env: CWI_HOME_NODE_TENANT_ID
  owner_user_id_env: CWI_HOME_NODE_OWNER_USER_ID
  friendly_name_env: CWI_HOME_NODE_FRIENDLY_NAME
""".strip(),
        encoding="utf-8",
    )

    config = load_config(config_path)

    assert config.control_session.tenant_id == "tenant-home-01"
    assert config.control_session.owner_user_id == "user-home-01"
    assert config.control_session.friendly_name == "home-node-07"
