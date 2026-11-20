from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

from node_agent_runtime.activation import ActivationHandshake, record_activation
from node_agent_runtime.cache import store_cached_asset
from node_agent_runtime.config import (
    ActivationConfig,
    AgentConfig,
    BaselinePacksConfig,
    BundleConfig,
    CacheConfig,
    DegradationConfig,
    EnrollmentConfig,
    EnrollmentTlsConfig,
    IdentityConfig,
    JobRunnerConfig,
    KillSwitchConfig,
    LocalModelConfig,
    StorageConfig,
)
from node_agent_runtime.local_ask import LocalAskHandler, describe_local_capabilities


def _base_config(tmp_path: Path) -> AgentConfig:
    state_dir = tmp_path / "state"
    return AgentConfig(
        coordinator_url="https://coord.example",
        enrollment_url="https://coord.example/api/membership/enroll",
        identity=IdentityConfig(
            node_id="node-01",
            private_key_path=None,
            public_key_path=None,
            key_type="ed25519",
            persist_keys=False,
        ),
        storage=StorageConfig(state_dir=state_dir),
        degradation=DegradationConfig(),
        activation=ActivationConfig(require_signature=False),
        kill_switch=KillSwitchConfig(state_path=state_dir / "kill_switch" / "state.json"),
        enrollment=EnrollmentConfig(
            request_timeout_seconds=10,
            protocol_version="1.0",
            cert_fingerprint="aa",
            tls=EnrollmentTlsConfig(),
        ),
    )


def _activate_node(config: AgentConfig, *, bundle_id: str = "bundle-1") -> None:
    handshake = ActivationHandshake(
        device_id=config.identity.node_id,
        bundle_id=bundle_id,
        nonce="nonce-1",
        expires_utc="2099-01-01T00:00:00Z",
        signature=None,
        claims={},
    )
    record_activation(config.storage.state_dir, handshake)


def test_local_ask_uses_bundle_evidence(tmp_path: Path):
    bundle_dir = tmp_path / "bundle"
    bundle_dir.mkdir(parents=True, exist_ok=True)
    (bundle_dir / "pack.json").write_text(
        json.dumps(
            {
                "pack_id": "thermal-pack",
                "pack_version": "1.0.0",
                "name": "Thermal Operations",
                "modules": [],
            },
            indent=2,
            sort_keys=True,
        ),
        encoding="utf-8",
    )
    (bundle_dir / "anchor.txt").write_text("anchor-123", encoding="utf-8")
    notes_dir = bundle_dir / "notes"
    notes_dir.mkdir(parents=True, exist_ok=True)
    (notes_dir / "cooling.md").write_text(
        "# Cooling\nCeramic cooling protects the Atlas engine during high heat load.\n",
        encoding="utf-8",
    )

    config = _base_config(tmp_path)
    config = AgentConfig(
        coordinator_url=config.coordinator_url,
        enrollment_url=config.enrollment_url,
        identity=config.identity,
        storage=config.storage,
        degradation=config.degradation,
        activation=config.activation,
        kill_switch=config.kill_switch,
        enrollment=config.enrollment,
        bundle=BundleConfig(unseal_dir=bundle_dir),
    )
    _activate_node(config, bundle_id="bundle-thermal")

    handler = LocalAskHandler(config)
    result = handler.run(
        {
            "question": "What protects the Atlas engine during high heat load?",
            "policy": {"local_only": True},
            "pack": {"hash": "pack-hash-1", "version": "1.0.0"},
        }
    )

    assert result.status == "completed"
    assert "local evidence" in result.details["answer"]
    assert result.details["sources"][0]["title"] == "notes/cooling.md"
    assert "Ceramic cooling" in result.details["sources"][0]["excerpt"]
    assert result.details["trace"]["bundle_id"] == "bundle-thermal"
    assert result.details["trace"]["pack_id"] == "thermal-pack"
    assert result.details["trace"]["pack_anchor"] == "anchor-123"
    assert result.details["trace"]["local_policy_outcome"] == "ALLOW_LOCAL_ONLY_NO_EGRESS"


def test_local_ask_uses_cached_artifact_evidence(tmp_path: Path):
    cache_dir = tmp_path / "cache"
    cache_dir.mkdir(parents=True, exist_ok=True)
    content = b"Project Atlas uses ceramic cooling on the local node."
    digest = hashlib.sha256(content).hexdigest()

    config = _base_config(tmp_path)
    config = AgentConfig(
        coordinator_url=config.coordinator_url,
        enrollment_url=config.enrollment_url,
        identity=config.identity,
        storage=config.storage,
        degradation=config.degradation,
        activation=config.activation,
        kill_switch=config.kill_switch,
        enrollment=config.enrollment,
        cache=CacheConfig(cache_dir=cache_dir),
    )
    store_cached_asset(
        config.cache,
        asset_id="atlas-memory",
        filename="memory.txt",
        data=content,
        sha256=digest,
    )

    handler = LocalAskHandler(config)
    result = handler.run({"question": "What uses ceramic cooling?"})

    assert result.status == "completed"
    assert result.details["sources"][0]["source_kind"] == "cache"
    assert result.details["sources"][0]["title"] == "cache/atlas-memory/memory.txt"
    assert "Project Atlas uses ceramic cooling" in result.details["sources"][0]["excerpt"]
    assert result.details["trace"]["execution_mode"] == "local_artifact_search"


def test_local_ask_uses_baseline_pack_evidence_for_identity_queries(tmp_path: Path):
    identity_root = tmp_path / "baseline" / "cwi-identity"
    identity_content_dir = identity_root / "content"
    identity_content_dir.mkdir(parents=True, exist_ok=True)
    (identity_root / "pack.json").write_text(
        json.dumps(
            {
                "pack_id": "cwi-identity",
                "pack_version": "1.0.0",
                "name": "CWI Identity",
                "retrieval": {
                    "force_match_queries": [
                        "what is cwi",
                        "what does cwi stand for",
                    ]
                },
                "modules": [
                    {
                        "role": "system_truth",
                    }
                ],
            },
            indent=2,
            sort_keys=True,
        ),
        encoding="utf-8",
    )
    (identity_root / "anchor.txt").write_text("identity-anchor", encoding="utf-8")
    (identity_content_dir / "cwi_identity.txt").write_text(
        "CWI stands for Christian Wheelwright Intelligence.",
        encoding="utf-8",
    )

    communication_root = tmp_path / "baseline" / "cwi-communication-core"
    communication_content_dir = communication_root / "content"
    communication_content_dir.mkdir(parents=True, exist_ok=True)
    (communication_root / "pack.json").write_text(
        json.dumps(
            {
                "pack_id": "cwi-communication-core",
                "pack_version": "1.0.0",
                "name": "CWI Communication Core",
                "modules": [
                    {
                        "role": "global_communication_layer",
                    }
                ],
            },
            indent=2,
            sort_keys=True,
        ),
        encoding="utf-8",
    )
    (communication_content_dir / "cwi_communication_core.txt").write_text(
        "Use clear, direct, friendly language when answering questions about CWI.",
        encoding="utf-8",
    )

    config = _base_config(tmp_path)
    config = AgentConfig(
        coordinator_url=config.coordinator_url,
        enrollment_url=config.enrollment_url,
        identity=config.identity,
        storage=config.storage,
        degradation=config.degradation,
        activation=config.activation,
        kill_switch=config.kill_switch,
        enrollment=config.enrollment,
        baseline_packs=BaselinePacksConfig(roots=(identity_root, communication_root)),
    )

    handler = LocalAskHandler(config)
    result = handler.run({"question": "What does CWI stand for?"})

    assert result.status == "completed"
    assert result.details["answer"].startswith("CWI stands for Christian Wheelwright Intelligence.")
    assert "Home node" not in result.details["answer"]
    assert result.details["sources"][0]["source_kind"] == "baseline"
    assert result.details["sources"][0]["title"] == "baseline/cwi-identity/content/cwi_identity.txt"
    assert result.details["sources"][0]["path"].replace("\\", "/").endswith(
        "/cwi-identity/content/cwi_identity.txt"
    )
    assert result.details["trace"]["baseline_pack_ids"] == [
        "cwi-identity",
        "cwi-communication-core",
    ]
    assert result.details["trace"]["baseline_pack_count"] == 2
    assert result.details["trace"]["baseline_source_count"] == 2
    assert result.details["trace"]["query_classification"] == "identity_definition"


def test_local_ask_prefers_latest_child_when_baseline_root_has_versions(tmp_path: Path):
    pack_root = tmp_path / "baseline" / "cwi-communication-core"
    old_content = pack_root / "1.0.0" / "content"
    latest_content = pack_root / "latest" / "content"
    old_content.mkdir(parents=True, exist_ok=True)
    latest_content.mkdir(parents=True, exist_ok=True)

    (pack_root / "1.0.0" / "pack.json").write_text(
        json.dumps(
            {
                "pack_id": "cwi-communication-core",
                "pack_version": "1.0.0",
                "name": "CWI Communication Core",
                "modules": [{"role": "global_communication_layer"}],
            },
            indent=2,
            sort_keys=True,
        ),
        encoding="utf-8",
    )
    (pack_root / "1.0.0" / "anchor.txt").write_text("old-anchor", encoding="utf-8")
    (old_content / "cwi_communication_core.txt").write_text(
        "Old communication rule: expose home node routing debug text.",
        encoding="utf-8",
    )

    (pack_root / "latest" / "pack.json").write_text(
        json.dumps(
            {
                "pack_id": "cwi-communication-core",
                "pack_version": "1.1.0",
                "name": "CWI Communication Core",
                "modules": [{"role": "global_communication_layer"}],
            },
            indent=2,
            sort_keys=True,
        ),
        encoding="utf-8",
    )
    (pack_root / "latest" / "anchor.txt").write_text("latest-anchor", encoding="utf-8")
    (latest_content / "cwi_communication_core.txt").write_text(
        "Current communication rule: do not expose home node routing debug text.",
        encoding="utf-8",
    )

    config = _base_config(tmp_path)
    config = AgentConfig(
        coordinator_url=config.coordinator_url,
        enrollment_url=config.enrollment_url,
        identity=config.identity,
        storage=config.storage,
        degradation=config.degradation,
        activation=config.activation,
        kill_switch=config.kill_switch,
        enrollment=config.enrollment,
        baseline_packs=BaselinePacksConfig(roots=(pack_root,)),
    )

    handler = LocalAskHandler(config)
    result = handler.run({"question": "What communication rule applies to home node debug text?"})

    assert result.status == "completed"
    assert "Current communication rule" in result.details["answer"]
    assert "Old communication rule" not in result.details["answer"]
    assert result.details["sources"][0]["title"] == "baseline/cwi-communication-core/content/cwi_communication_core.txt"
    assert result.details["trace"]["baseline_pack_ids"] == ["cwi-communication-core"]


def test_local_ask_does_not_match_generic_deploy_node_prompt_to_memory_pack(tmp_path: Path):
    memory_root = tmp_path / "baseline" / "cwi-memory-capsule"
    memory_content_dir = memory_root / "content"
    memory_content_dir.mkdir(parents=True, exist_ok=True)
    (memory_root / "pack.json").write_text(
        json.dumps(
            {
                "pack_id": "cwi-memory-capsule",
                "pack_version": "1.0.0",
                "name": "CWI Memory Capsule",
                "modules": [{"role": "memory_capsule_layer"}],
            },
            indent=2,
            sort_keys=True,
        ),
        encoding="utf-8",
    )
    (memory_content_dir / "memory_capsule.txt").write_text(
        "Durable memory mentions home nodes, deployments, and local runtime plans.",
        encoding="utf-8",
    )

    config = _base_config(tmp_path)
    config = AgentConfig(
        coordinator_url=config.coordinator_url,
        enrollment_url=config.enrollment_url,
        identity=config.identity,
        storage=config.storage,
        degradation=config.degradation,
        activation=config.activation,
        kill_switch=config.kill_switch,
        enrollment=config.enrollment,
        baseline_packs=BaselinePacksConfig(roots=(memory_root,)),
    )

    handler = LocalAskHandler(config)
    result = handler.run({"question": "Give me a plan to deploy a node"})

    assert result.status == "completed"
    assert result.details["sources"] == []
    assert "Durable memory" not in result.details["answer"]
    assert result.details["trace"]["query_classification"] is None


def test_local_ask_explicit_job_uses_runner(tmp_path: Path):
    config = _base_config(tmp_path)
    config = AgentConfig(
        coordinator_url=config.coordinator_url,
        enrollment_url=config.enrollment_url,
        identity=config.identity,
        storage=config.storage,
        degradation=config.degradation,
        activation=config.activation,
        kill_switch=config.kill_switch,
        enrollment=config.enrollment,
        job_runner=JobRunnerConfig(
            allow_commands=(sys.executable,),
            default_timeout_seconds=5,
        ),
    )

    handler = LocalAskHandler(config)
    result = handler.run(
        {
            "question": "Run local health check",
            "job": {"command": [sys.executable, "-c", "print('local job ok')"]},
            "data_classification": "REMOTE_ALLOWED",
        }
    )

    assert result.status == "completed"
    assert result.details["answer"] == "local job ok"
    assert result.details["trace"]["execution_mode"] == "job_runner"
    assert result.details["trace"]["local_policy_outcome"] == "ALLOW_LOCAL_PLUS_REMOTE_ASSIST"


def test_describe_local_capabilities_includes_retrieval_when_artifacts_available(tmp_path: Path):
    bundle_dir = tmp_path / "bundle"
    bundle_dir.mkdir(parents=True, exist_ok=True)
    (bundle_dir / "pack.json").write_text(
        json.dumps({"pack_id": "pack-1", "pack_version": "1.0.0", "name": "Pack", "modules": []}),
        encoding="utf-8",
    )

    config = _base_config(tmp_path)
    config = AgentConfig(
        coordinator_url=config.coordinator_url,
        enrollment_url=config.enrollment_url,
        identity=config.identity,
        storage=config.storage,
        degradation=config.degradation,
        activation=config.activation,
        kill_switch=config.kill_switch,
        enrollment=config.enrollment,
        bundle=BundleConfig(unseal_dir=bundle_dir),
    )

    capabilities = describe_local_capabilities(config)
    assert capabilities == ["local_ask", "local_retrieval"]


def test_describe_local_capabilities_includes_model_when_enabled(tmp_path: Path):
    config = _base_config(tmp_path)
    config = AgentConfig(
        coordinator_url=config.coordinator_url,
        enrollment_url=config.enrollment_url,
        identity=config.identity,
        storage=config.storage,
        degradation=config.degradation,
        activation=config.activation,
        kill_switch=config.kill_switch,
        enrollment=config.enrollment,
        local_model=LocalModelConfig(enabled=True, model="local-8b:q4"),
    )

    capabilities = describe_local_capabilities(config)

    assert capabilities == ["local_ask", "local_model", "local_model_ollama"]


def test_local_ask_uses_local_model_when_route_requests_it(tmp_path: Path, monkeypatch):
    config = _base_config(tmp_path)
    config = AgentConfig(
        coordinator_url=config.coordinator_url,
        enrollment_url=config.enrollment_url,
        identity=config.identity,
        storage=config.storage,
        degradation=config.degradation,
        activation=config.activation,
        kill_switch=config.kill_switch,
        enrollment=config.enrollment,
        local_model=LocalModelConfig(
            enabled=True,
            base_url="http://ollama:11434",
            model="local-8b:q4",
            context_tokens=4096,
        ),
    )
    captured: dict[str, object] = {}

    class _Response:
        def __enter__(self):
            return self

        def __exit__(self, exc_type, exc, tb):
            return False

        def read(self):
            return b'{"model":"local-8b:q4","response":"local model answer"}'

    def _urlopen(req, timeout):
        captured["url"] = req.full_url
        captured["body"] = json.loads(req.data.decode("utf-8"))
        captured["timeout"] = timeout
        return _Response()

    monkeypatch.setattr("node_agent_runtime.local_model.request.urlopen", _urlopen)

    result = LocalAskHandler(config).run(
        {
            "question": "Summarize private notes",
            "execution": {
                "route_mode": "local_model",
                "route_reason": "small_quantized_home_node_profile",
                "max_context_tokens": 2048,
            },
        }
    )

    assert result.status == "completed"
    assert result.details["answer"] == "local model answer"
    assert result.details["trace"]["execution_mode"] == "local_model_ollama"
    assert result.details["trace"]["local_model"] == "local-8b:q4"
    assert result.details["trace"]["grounded"] is False
    assert result.details["trace"]["reasoning_based"] is True
    assert captured["url"] == "http://ollama:11434/api/generate"
    assert captured["body"]["options"]["num_ctx"] == 2048


def test_local_ask_falls_back_to_local_model_when_no_evidence(tmp_path: Path, monkeypatch):
    config = _base_config(tmp_path)
    config = AgentConfig(
        coordinator_url=config.coordinator_url,
        enrollment_url=config.enrollment_url,
        identity=config.identity,
        storage=config.storage,
        degradation=config.degradation,
        activation=config.activation,
        kill_switch=config.kill_switch,
        enrollment=config.enrollment,
        local_model=LocalModelConfig(
            enabled=True,
            base_url="http://ollama:11434",
            model="local-8b:q4",
            context_tokens=4096,
        ),
    )
    captured: dict[str, object] = {}

    class _Response:
        def __enter__(self):
            return self

        def __exit__(self, exc_type, exc, tb):
            return False

        def read(self):
            return b'{"model":"local-8b:q4","response":"4"}'

    def _urlopen(req, timeout):
        captured["body"] = json.loads(req.data.decode("utf-8"))
        return _Response()

    monkeypatch.setattr("node_agent_runtime.local_model.request.urlopen", _urlopen)

    result = LocalAskHandler(config).run({"question": "What is 2 + 2?"})

    assert result.status == "completed"
    assert result.details["answer"] == "4"
    assert result.details["sources"] == []
    assert result.details["trace"]["execution_mode"] == "local_model_ollama"
    assert result.details["trace"]["source_count"] == 0
    assert result.details["trace"]["grounded"] is False
    assert result.details["trace"]["reasoning_based"] is True
    assert "Grounding mode: non_grounded_reasoning" in captured["body"]["prompt"]
    assert "still answer general knowledge, math" in captured["body"]["prompt"]
