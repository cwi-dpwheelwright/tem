from __future__ import annotations

from datetime import datetime, timedelta, timezone
import json
from pathlib import Path

from node_agent_runtime.activation import ActivationHandshake, record_activation
from node_agent_runtime.config import (
    AgentConfig,
    ControlSessionConfig,
    IdentityConfig,
    StorageConfig,
)
from node_agent_runtime.control_channel import create_runtime, build_node_hello
from node_agent_runtime.process_miner import (
    LocalProcessMiner,
    ProcessMinerSummaryRequest,
    resolve_process_miner_dir,
)


def _base_config(tmp_path: Path) -> AgentConfig:
    return AgentConfig(
        coordinator_url="https://coord.example",
        enrollment_url="https://coord.example/api/membership/enroll",
        identity=IdentityConfig(
            node_id="node-1",
            private_key_path=None,
            public_key_path=None,
            key_type="ed25519",
            persist_keys=False,
        ),
        storage=StorageConfig(state_dir=tmp_path),
        control_session=ControlSessionConfig(
            tenant_id="tenant-1",
            owner_user_id="user-1",
            work_token_secret="secret",
        ),
    )


def _activate_node(tmp_path: Path) -> None:
    expires = (datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat()
    expires = expires.replace("+00:00", "Z")
    handshake = ActivationHandshake(
        device_id="node-1",
        bundle_id="bundle-1",
        nonce="nonce-1",
        expires_utc=expires,
        signature=None,
        claims={},
    )
    record_activation(tmp_path, handshake)


def test_process_miner_records_sanitized_local_observations(tmp_path: Path):
    miner = LocalProcessMiner(tmp_path)

    result = miner.observe(
        [
            {
                "type": "open_loop",
                "signal": "assignment_due",
                "count": 2,
                "confidence": 0.82,
                "tags": ["school", "tomorrow"],
                "raw_text": "Private raw content must not be persisted.",
            },
            {
                "type": "relationship",
                "signal": "group_partner",
                "message": "Private message must not be persisted.",
            },
        ]
    )

    assert result.status == "observed"
    assert result.memory_mutated is False
    assert result.graph_mutated is False
    assert result.raw_content_exported is False

    output_path = resolve_process_miner_dir(tmp_path) / "observations.jsonl"
    contents = output_path.read_text(encoding="utf-8")
    assert "assignment_due" in contents
    assert "group_partner" in contents
    assert "Private raw content" not in contents
    assert "Private message" not in contents


def test_process_miner_summary_requires_approved_policy(tmp_path: Path):
    miner = LocalProcessMiner(tmp_path)

    result = miner.observe(
        [{"type": "routine", "signal": "morning_prep", "confidence": 0.7}],
        policy_status="observe",
        summary_request=ProcessMinerSummaryRequest(
            tenant_id="tenant-1",
            node_id="node-1",
            user_id_hash="user-hash",
            trace_id="trace-1",
            policy_status="observe",
        ),
    )

    assert result.summary is not None
    assert result.summary["approved_for_export"] is False
    assert "policy_status_not_approved" in result.summary["reasons"]
    assert result.summary["raw_content_exported"] is False


def test_process_miner_summary_uses_federated_learning_shape_when_approved(tmp_path: Path):
    miner = LocalProcessMiner(tmp_path)

    result = miner.observe(
        [{"type": "document", "signal": "class_notes", "confidence": 0.91}],
        summary_request=ProcessMinerSummaryRequest(
            tenant_id="tenant-1",
            node_id="node-1",
            user_id_hash="user-hash",
            trace_id="trace-1",
            policy_status="approved",
        ),
    )

    assert result.summary is not None
    assert result.summary["approved_for_export"] is True
    assert result.summary["observations"][0]["category"] == "document"
    assert result.summary["observations"][0]["signal"] == "class_notes"
    assert result.summary["raw_content_exported"] is False

    summary_path = resolve_process_miner_dir(tmp_path) / "summaries.jsonl"
    stored = json.loads(summary_path.read_text(encoding="utf-8").splitlines()[0])
    assert stored["schema_version"] == "local_process_miner_summary.v1"


def test_process_miner_task_is_registered_and_observe_only(tmp_path: Path):
    _activate_node(tmp_path)
    config = _base_config(tmp_path)
    runtime = create_runtime(config)

    result = runtime.run(
        "process_miner.observe",
        {
            "policy_status": "approved",
            "emit_summary": True,
            "tenant_id": "tenant-1",
            "node_id": "node-1",
            "trace_id": "trace-1",
            "observations": [
                {"type": "anomaly", "signal": "missed_deadline", "confidence": 0.76}
            ],
        },
    )

    assert result.status == "completed"
    assert result.details["memory_mutated"] is False
    assert result.details["graph_mutated"] is False
    assert result.details["summary"]["approved_for_export"] is True


def test_process_miner_task_blocks_without_state_dir():
    miner = LocalProcessMiner(None)
    result = miner.observe([{"type": "routine", "signal": "morning_prep"}])

    assert result.status == "blocked"
    assert "state_dir_required" in result.reasons


def test_process_miner_task_respects_raw_data_boundary(tmp_path: Path):
    _activate_node(tmp_path)
    runtime = create_runtime(_base_config(tmp_path))

    result = runtime.run(
        "process_miner.observe",
        {
            "raw_data": True,
            "observations": [{"type": "routine", "signal": "morning_prep"}],
        },
    )

    assert result.status == "blocked"
    assert "raw_data_blocked" in result.details["reasons"]


def test_node_hello_advertises_process_miner_observe_only_capability(tmp_path: Path):
    hello = build_node_hello(_base_config(tmp_path))

    assert "local_process_miner" in hello["capabilities"]
    assert "local_process_miner_observe_only" in hello["capabilities"]
