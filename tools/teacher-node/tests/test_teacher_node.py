import hashlib
import json
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools" / "teacher-node"))

import teacher_node  # noqa: E402
from artifact_store import FileSystemArtifactStore  # noqa: E402
from teacher_critic import run_teacher_critic  # noqa: E402


def test_anchor_matches_compose_packs():
    packs_root = ROOT / "OPS" / "cwi-ops" / "compose" / "packs"
    for pack_dir in [packs_root / "enterprise", packs_root / "personal"]:
        pack_path = pack_dir / "pack.json"
        anchor_path = pack_dir / "anchor.txt"
        assert pack_path.exists()
        assert anchor_path.exists()
        anchor = teacher_node.compute_anchor(pack_path)
        expected = anchor_path.read_text(encoding="utf-8").strip()
        assert anchor == expected


def test_validate_pack_schema():
    schema_path = ROOT / "contracts" / "json-schema" / "pack_schema.json"
    pack_path = ROOT / "OPS" / "cwi-ops" / "compose" / "packs" / "enterprise" / "pack.json"
    teacher_node.validate_pack(pack_path, schema_path)


def test_teacher_health_contract_reports_required_dependencies(tmp_path, monkeypatch):
    artifact_root = tmp_path / "artifacts"
    graph_root = tmp_path / "graph-staging"
    graph_root.mkdir()
    validation_marker = tmp_path / "last-validation.json"
    validation_marker.write_text(
        json.dumps({"timestamp_utc": "2026-05-06T12:00:00Z"}),
        encoding="utf-8",
    )
    monkeypatch.setenv("TEACHER_NODE_ARTIFACT_BACKEND", "filesystem")
    monkeypatch.setenv("TEACHER_NODE_ARTIFACT_ROOT", str(artifact_root))
    monkeypatch.setenv("TEACHER_NODE_GRAPH_STAGING_ROOT", str(graph_root))
    monkeypatch.setenv("TEACHER_NODE_LAST_SUCCESSFUL_VALIDATION_PATH", str(validation_marker))

    status_code, payload = teacher_node.build_teacher_health_payload()

    assert status_code == 200
    assert payload["status"] == "healthy"
    assert payload["checks"]["teacher_app"]["status"] == "healthy"
    assert payload["checks"]["artifact_store"]["status"] == "healthy"
    assert payload["checks"]["graph_staging"]["status"] == "healthy"
    assert payload["last_successful_validation_utc"] == "2026-05-06T12:00:00Z"


def test_teacher_health_contract_degrades_when_graph_staging_is_unavailable(tmp_path, monkeypatch):
    artifact_root = tmp_path / "artifacts"
    missing_graph_root = tmp_path / "missing-graph-staging"
    monkeypatch.setenv("TEACHER_NODE_ARTIFACT_BACKEND", "filesystem")
    monkeypatch.setenv("TEACHER_NODE_ARTIFACT_ROOT", str(artifact_root))
    monkeypatch.setenv("TEACHER_NODE_GRAPH_STAGING_ROOT", str(missing_graph_root))
    monkeypatch.delenv("TEACHER_NODE_LAST_SUCCESSFUL_VALIDATION_PATH", raising=False)

    status_code, payload = teacher_node.build_teacher_health_payload()

    assert status_code == 503
    assert payload["status"] == "degraded"
    assert payload["checks"]["artifact_store"]["status"] == "healthy"
    assert payload["checks"]["graph_staging"]["status"] == "degraded"
    assert payload["checks"]["last_successful_validation"]["status"] == "unknown"


def test_federated_learning_summary_aggregates_without_raw_observations(tmp_path):
    payload = {
        "schema_version": "federated_learning_summary.v1",
        "summary_id": "fed-sum-test",
        "tenant_id": "tenant-1",
        "user_id_hash": "hash-user",
        "node_id": "node-1",
        "trace_id": "trace-1",
        "policy_status": "approved",
        "source_component": "local_process_miner",
        "generated_utc": "2026-05-06T12:00:00Z",
        "raw_content_exported": False,
        "summary_groups": [
            {
                "category": "routine",
                "signal": "open_loop",
                "outcome": "observed",
                "count": 2,
                "average_confidence": 0.9,
                "tags": ["morning"],
            }
        ],
        "provenance": {
            "source_hash": "abc123",
            "generated_utc": "2026-05-06T12:00:00Z",
        },
    }

    status_code, response = teacher_node.record_federated_learning_summary(payload, tmp_path)

    assert status_code == 202
    assert response["status"] == "accepted"
    aggregate = json.loads((tmp_path / "aggregate.json").read_text(encoding="utf-8"))
    assert aggregate["summary_count"] == 1
    assert aggregate["observation_count"] == 2
    assert aggregate["category_counts"]["routine"] == 2
    stored = (tmp_path / "summaries.jsonl").read_text(encoding="utf-8")
    assert "raw_text" not in stored
    assert "conversation" not in stored


def test_federated_learning_summary_rejects_raw_payload_keys(tmp_path):
    payload = {
        "schema_version": "federated_learning_summary.v1",
        "summary_id": "fed-sum-test",
        "tenant_id": "tenant-1",
        "trace_id": "trace-1",
        "policy_status": "approved",
        "raw_content_exported": False,
        "summary_groups": [
            {
                "category": "routine",
                "signal": "open_loop",
                "outcome": "observed",
                "count": 1,
                "tags": [],
            }
        ],
        "observations": [{"raw_text": "private sentence"}],
        "provenance": {"source_hash": "abc123"},
    }

    status_code, response = teacher_node.record_federated_learning_summary(payload, tmp_path)

    assert status_code == 400
    assert response["reason"] == "unsafe_raw_content_field"
    assert not (tmp_path / "summaries.jsonl").exists()


def test_teacher_critic_validates_pack_artifacts_and_vectors(tmp_path):
    pack_dir = tmp_path / "packs" / "demo"
    pack_dir.mkdir(parents=True)
    pack = {
        "pack_id": "demo-pack",
        "pack_version": "1.0.0",
        "name": "Demo Pack",
        "modules": [],
    }
    (pack_dir / "pack.json").write_text(json.dumps(pack), encoding="utf-8")
    anchor = teacher_node.compute_anchor(pack_dir / "pack.json")
    (pack_dir / "anchor.txt").write_text(anchor + "\n", encoding="utf-8")

    artifact_root = tmp_path / "artifacts"
    artifact_root.mkdir()
    (artifact_root / "manifest.json").write_text(
        json.dumps({"pack_id": "demo-pack", "anchor": anchor}),
        encoding="utf-8",
    )

    vector_root = tmp_path / "vectors"
    vector_root.mkdir()
    (vector_root / "qdrant-export.jsonl").write_text(
        json.dumps({"payload": {"pack_id": "demo-pack", "pack_anchor": anchor}}) + "\n",
        encoding="utf-8",
    )

    report = run_teacher_critic(
        pack_roots=(tmp_path / "packs",),
        artifact_root=artifact_root,
        vector_root=vector_root,
    )

    assert report["status"] == "pass"
    codes = {finding["code"] for finding in report["findings"]}
    assert "anchor_verified" in codes
    assert "artifact_json_valid" in codes
    assert "vector_anchors_consistent" in codes


def test_teacher_critic_fails_anchor_mismatch(tmp_path):
    pack_dir = tmp_path / "packs" / "demo"
    pack_dir.mkdir(parents=True)
    (pack_dir / "pack.json").write_text(
        json.dumps(
            {
                "pack_id": "demo-pack",
                "pack_version": "1.0.0",
                "name": "Demo Pack",
                "modules": [],
            }
        ),
        encoding="utf-8",
    )
    (pack_dir / "anchor.txt").write_text("0" * 64 + "\n", encoding="utf-8")

    report = run_teacher_critic(
        pack_roots=(tmp_path / "packs",),
        artifact_root=None,
        vector_root=None,
    )

    assert report["status"] == "fail"
    assert any(finding["code"] == "anchor_mismatch" for finding in report["findings"])


def test_teacher_critic_cli_returns_nonzero_on_failures(tmp_path):
    pack_dir = tmp_path / "packs" / "demo"
    pack_dir.mkdir(parents=True)
    (pack_dir / "pack.json").write_text("{invalid", encoding="utf-8")

    args = type(
        "Args",
        (),
        {
            "pack_root": [str(tmp_path / "packs")],
            "artifact_root": None,
            "vector_root": None,
            "out": str(tmp_path / "critic.json"),
            "validation_marker": str(tmp_path / "marker.json"),
        },
    )

    assert teacher_node.cmd_critic_run(args) == 1
    report = json.loads((tmp_path / "critic.json").read_text(encoding="utf-8"))
    assert report["status"] == "fail"
    assert not (tmp_path / "marker.json").exists()


def test_dependency_graph_fixture():
    fixture = ROOT / "tools" / "teacher-node" / "tests" / "fixtures" / "pack_with_deps.json"
    graph = teacher_node.build_dependency_graph(fixture)
    assert graph["pack_id"] == "fixture-pack"
    assert graph["pack_version"] == "1.2.3"
    assert graph["dependencies"] == [
        {"pack_id": "core-pack", "pack_version": "0.9.1"},
        {"pack_id": "extra-pack", "pack_version": "2.0.0"},
    ]


def test_publish_and_rollback(tmp_path):
    pack_dir = tmp_path / "pack"
    pack_dir.mkdir(parents=True)
    pack_data = {
        "pack_id": "fixture-pack",
        "pack_version": "1.0.0",
        "name": "Fixture Pack",
        "modules": [],
    }
    (pack_dir / "pack.json").write_text(json.dumps(pack_data), encoding="utf-8")

    out_dir = tmp_path / "registry"
    args = type("Args", (), {
        "pack_dir": str(pack_dir),
        "out_dir": str(out_dir),
        "version": None,
        "signing_key": "dev-key",
        "published_utc": "2026-01-30T00:00:00Z",
        "transport": "copy",
        "dry_run": False,
        "published_layout": False,
        "report_bundle": None,
        "run_id": None,
    })
    assert teacher_node.cmd_publish(args) == 0

    manifest_path = out_dir / "fixture-pack" / "1.0.0" / "manifest.json"
    pack_path = out_dir / "fixture-pack" / "1.0.0" / "pack.json"
    anchor_path = out_dir / "fixture-pack" / "1.0.0" / "anchor.txt"
    assert manifest_path.exists()
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    assert manifest["pack_id"] == "fixture-pack"
    assert manifest["pack_version"] == "1.0.0"
    assert manifest["anchor"]
    assert (
        hashlib.sha256(pack_path.read_bytes()).hexdigest()
        == anchor_path.read_text(encoding="utf-8").strip()
    )

    rollback_args = type("Args", (), {
        "registry_dir": str(out_dir),
        "pack_id": "fixture-pack",
        "version": "0.9.0",
        "reason": "test",
    })
    assert teacher_node.cmd_rollback(rollback_args) == 0

    pin_path = out_dir / "pins" / "fixture-pack.json"
    assert pin_path.exists()


def test_publish_published_layout(tmp_path):
    pack_dir = tmp_path / "pack"
    pack_dir.mkdir(parents=True)
    pack_data = {
        "pack_id": "fixture-pack",
        "pack_version": "1.0.0",
        "name": "Fixture Pack",
        "modules": [],
    }
    (pack_dir / "pack.json").write_text(json.dumps(pack_data), encoding="utf-8")

    out_dir = tmp_path / "registry"
    published_root = tmp_path / "published"
    published_store = FileSystemArtifactStore(published_root)

    result = teacher_node.publish_pack(
        pack_dir=pack_dir,
        out_dir=out_dir,
        transport="copy",
        published_store=published_store,
    )

    anchor = result["anchor"]
    latest_path = published_root / "fixture-pack" / "latest.txt"
    pack_path = published_root / "fixture-pack" / anchor / "pack.json"
    anchor_path = published_root / "fixture-pack" / anchor / "anchor.txt"

    assert latest_path.read_text(encoding="utf-8").strip() == anchor
    assert anchor_path.read_text(encoding="utf-8").strip() == anchor
    assert hashlib.sha256(pack_path.read_bytes()).hexdigest() == anchor
    assert json.loads(pack_path.read_text(encoding="utf-8"))["pack_id"] == "fixture-pack"
