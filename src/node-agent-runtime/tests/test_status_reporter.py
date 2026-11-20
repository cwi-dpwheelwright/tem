from __future__ import annotations

from node_agent_runtime.config import AgentConfig, IdentityConfig, StorageConfig, StatusConfig
from node_agent_runtime.status_reporter import build_status_report, record_status


def test_status_reporter_records(tmp_path):
    config = AgentConfig(
        coordinator_url="https://coord",
        enrollment_url="https://coord/api/membership/enroll",
        identity=IdentityConfig(
            node_id="node-1",
            private_key_path=None,
            public_key_path=None,
            key_type="ed25519",
            persist_keys=False,
        ),
        storage=StorageConfig(state_dir=tmp_path),
        status=StatusConfig(status_url=None, timeout_seconds=1),
    )
    report = build_status_report(config)
    path = record_status(report, tmp_path)
    assert path is not None
    assert path.exists()
