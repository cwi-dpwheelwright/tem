from __future__ import annotations

import hashlib

import pytest

from node_agent_runtime.config import ConfigError, load_config
from node_agent_runtime.node_id import derive_node_id_from_machine_id, normalize_machine_id


def test_normalize_machine_id_strips_whitespace_and_lowercases() -> None:
    assert normalize_machine_id("ABCDEF01 23456789\nabcdef0123456789\r\n") == "abcdef0123456789abcdef0123456789"


def test_derive_node_id_from_machine_id_hashes_machine_value(tmp_path) -> None:
    machine_id_path = tmp_path / "machine-id"
    machine_id_path.write_text("0123456789ABCDEF0123456789ABCDEF\n", encoding="utf-8")

    expected = hashlib.sha256(b"0123456789abcdef0123456789abcdef").hexdigest()[:32]
    assert derive_node_id_from_machine_id(machine_id_path) == f"home-{expected}"


def test_load_config_derives_identity_node_id_from_machine_id(tmp_path) -> None:
    machine_id_path = tmp_path / "machine-id"
    machine_id_path.write_text("0123456789abcdef0123456789abcdef\n", encoding="utf-8")
    config_path = tmp_path / "node-agent.yaml"
    config_path.write_text(
        f"""
coordinator_url: https://coord
enrollment_url: https://coord/api/membership/enroll
identity:
  node_id: auto
  machine_id_path: "{machine_id_path.as_posix()}"
  persist_keys: false
""".strip(),
        encoding="utf-8",
    )

    config = load_config(config_path)

    expected = hashlib.sha256(b"0123456789abcdef0123456789abcdef").hexdigest()[:32]
    assert config.identity.node_id == f"home-{expected}"


def test_load_config_rejects_auto_node_id_when_machine_identifier_missing(tmp_path) -> None:
    config_path = tmp_path / "node-agent.yaml"
    missing_path = tmp_path / "missing-machine-id"
    config_path.write_text(
        f"""
coordinator_url: https://coord
enrollment_url: https://coord/api/membership/enroll
identity:
  node_id: auto
  machine_id_path: "{missing_path.as_posix()}"
  persist_keys: false
""".strip(),
        encoding="utf-8",
    )

    with pytest.raises(ConfigError, match="Unable to derive identity.node_id automatically"):
        load_config(config_path)


def test_load_config_reads_baseline_pack_roots(tmp_path) -> None:
    baseline_root = tmp_path / "baseline" / "cwi-identity"
    baseline_root.mkdir(parents=True, exist_ok=True)
    config_path = tmp_path / "node-agent.yaml"
    config_path.write_text(
        f"""
coordinator_url: https://coord
enrollment_url: https://coord/api/membership/enroll
identity:
  node_id: node-01
  persist_keys: false
baseline_packs:
  roots:
    - "{baseline_root.as_posix()}"
""".strip(),
        encoding="utf-8",
    )

    config = load_config(config_path)

    assert config.baseline_packs.roots == (baseline_root,)


def test_load_config_reads_local_model_profile_from_env(tmp_path, monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("CWI_HOME_NODE_LOCAL_MODEL", "local-8b:q4")
    config_path = tmp_path / "node-agent.yaml"
    config_path.write_text(
        """
coordinator_url: https://coord
enrollment_url: https://coord/api/membership/enroll
identity:
  node_id: node-01
  persist_keys: false
local_model:
  enabled: true
  provider: ollama
  base_url: http://ollama:11434
  model_env: CWI_HOME_NODE_LOCAL_MODEL
  model_family: 7b/8b-instruct
  quantization: Q5_K_M
  context_tokens: 4096
  max_vram_gb: 6
  timeout_seconds: 30
""".strip(),
        encoding="utf-8",
    )

    config = load_config(config_path)

    assert config.local_model.enabled is True
    assert config.local_model.model == "local-8b:q4"
    assert config.local_model.quantization == "Q5_K_M"
    assert config.local_model.context_tokens == 4096
    assert config.local_model.max_vram_gb == 6
    assert config.local_model.timeout_seconds == 30
