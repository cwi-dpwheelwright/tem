from __future__ import annotations

import hashlib

from node_agent_runtime.config import (
    AgentConfig,
    IdentityConfig,
    KillSwitchConfig,
    StorageConfig,
    TamperConfig,
)
from node_agent_runtime.kill_switch import load_kill_switch_state
from node_agent_runtime.tamper import (
    apply_tamper_policy,
    decide_tamper_policy,
    evaluate_tamper,
)


def _base_config(tmp_path, *, tamper: TamperConfig, kill_switch: KillSwitchConfig | None = None) -> AgentConfig:
    return AgentConfig(
        coordinator_url="https://coord",
        enrollment_url="https://coord/api/membership/enroll",
        identity=IdentityConfig(
            node_id="node-01",
            private_key_path=None,
            public_key_path=None,
            key_type="ed25519",
            persist_keys=False,
        ),
        storage=StorageConfig(state_dir=tmp_path),
        kill_switch=kill_switch or KillSwitchConfig(),
        tamper=tamper,
    )


def test_evaluate_tamper_missing_file(tmp_path):
    config = TamperConfig(indicator_files_exist=(tmp_path / "missing.txt",))
    assessment = evaluate_tamper(config)
    assert assessment.tampered == 1
    assert assessment.severity == 1.0
    assert assessment.indicators[0].status == "missing"


def test_evaluate_tamper_hash_mismatch(tmp_path):
    path = tmp_path / "seal.bin"
    path.write_text("ok", encoding="utf-8")
    expected = hashlib.sha256(b"ok").hexdigest()
    config = TamperConfig(indicator_files_sha256={path: expected})
    assessment = evaluate_tamper(config)
    assert assessment.tampered == 0
    assert assessment.severity == 0.0

    mismatch_config = TamperConfig(indicator_files_sha256={path: "0" * 64})
    mismatch = evaluate_tamper(mismatch_config)
    assert mismatch.tampered == 1
    assert mismatch.indicators[0].status == "mismatch"


def test_tamper_policy_ticket_writes(tmp_path, monkeypatch):
    monkeypatch.setenv("NODE_AGENT_TAMPER", "1")
    tamper = TamperConfig(
        indicator_env_vars=("NODE_AGENT_TAMPER",),
        policy_action="ticket",
        policy_minimum_severity=0.5,
    )
    config = _base_config(tmp_path, tamper=tamper)
    assessment = evaluate_tamper(config.tamper)
    decision = decide_tamper_policy(assessment, config.tamper)
    policy = apply_tamper_policy(
        decision,
        config=config,
        observed_utc=assessment.observed_utc,
        event_id="evt-1",
    )
    assert policy.applied is True
    assert (tmp_path / "tamper" / "tickets.jsonl").exists()


def test_tamper_policy_kill_irreversible_sets_state(tmp_path):
    tamper = TamperConfig(
        indicator_files_exist=(tmp_path / "missing.txt",),
        policy_action="kill_irreversible",
        policy_minimum_severity=0.5,
    )
    kill_switch = KillSwitchConfig(state_path=tmp_path / "kill_switch" / "state.json")
    config = _base_config(tmp_path, tamper=tamper, kill_switch=kill_switch)
    assessment = evaluate_tamper(config.tamper)
    decision = decide_tamper_policy(assessment, config.tamper)
    policy = apply_tamper_policy(
        decision,
        config=config,
        observed_utc=assessment.observed_utc,
        event_id="evt-2",
    )
    assert policy.applied is True
    state = load_kill_switch_state(kill_switch.state_path)
    assert state is not None
    assert state.enabled is True
    assert state.irreversible is True
