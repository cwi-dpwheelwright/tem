import json
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[3]

import sys
sys.path.insert(0, str(ROOT / "tools" / "teacher-node"))

from artifact_store import FileSystemArtifactStore
from capability_map import write_capability_map
from coherence import check_coherence
from reports import write_report_bundle
from signing import sign_report_bundle


def test_capability_map_generation(tmp_path):
    pack_path = tmp_path / "pack.json"
    pack = {
        "pack_id": "cap-pack",
        "pack_version": "1.0.0",
        "name": "Cap Pack",
        "modules": [],
        "capabilities": [
            {"capability": "ask", "actions": ["ask"], "permissions": ["read"], "limits": {"rate": 10}}
        ],
    }
    pack_path.write_text(json.dumps(pack), encoding="utf-8")
    out_path = tmp_path / "capability-map.json"

    cap_map = write_capability_map(pack_path, out_path)

    assert out_path.exists()
    assert cap_map["pack_id"] == "cap-pack"
    assert cap_map["capabilities"][0]["capability"] == "ask"


def test_report_generation_bundle_and_signing(tmp_path):
    input_data = {
        "pack_id": "report-pack",
        "pack_version": "2.0.0",
        "run_id": "run-123",
        "metrics": {"accuracy": 0.9},
        "findings": [{"severity": "low", "message": "minor"}],
    }

    report_paths = write_report_bundle(input_data, tmp_path)
    assert report_paths["report_json"].exists()
    assert report_paths["report_md"].exists()

    # signing
    key_path = tmp_path / "signing.pem"
    _write_ed25519_key(key_path)
    signature = sign_report_bundle(report_paths["report_json"], report_paths["report_md"], key_path)
    assert signature["signature"]


def test_coherence_detects_conflict():
    structured = {
        "summary": {"overall_status": "fail"},
        "findings": [{"severity": "high", "message": "issue"}],
    }
    narrative = "All checks pass with no issues."
    coherence = check_coherence(structured, narrative)
    assert coherence["status"] in ("fail", "warn")
    assert coherence["conflicts"]


def test_artifact_store_filesystem(tmp_path):
    store = FileSystemArtifactStore(tmp_path)
    source = tmp_path / "source.txt"
    source.write_text("data", encoding="utf-8")

    store.upload(source, "reports/source.txt")
    dest = tmp_path / "dest.txt"
    store.download("reports/source.txt", dest)

    assert dest.read_text(encoding="utf-8") == "data"


def _write_ed25519_key(path: Path) -> None:
    try:
        from cryptography.hazmat.primitives import serialization
        from cryptography.hazmat.primitives.asymmetric import ed25519
    except ImportError as exc:
        pytest.skip("cryptography required for signing tests")

    key = ed25519.Ed25519PrivateKey.generate()
    pem = key.private_bytes(
        encoding=serialization.Encoding.PEM,
        format=serialization.PrivateFormat.PKCS8,
        encryption_algorithm=serialization.NoEncryption(),
    )
    path.write_bytes(pem)
