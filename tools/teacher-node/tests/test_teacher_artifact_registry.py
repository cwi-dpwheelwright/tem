import sys
from pathlib import Path

import pytest


ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools" / "teacher-node"))

from artifact_registry import build_artifact_payload, resolve_tenant_id  # noqa: E402


def test_resolve_tenant_id_from_input():
    payload = {"tenant_id": "tenant-a"}
    assert resolve_tenant_id(payload) == "tenant-a"


def test_resolve_tenant_id_from_env(monkeypatch):
    monkeypatch.setenv("TEACHER_NODE_TENANT_ID", "tenant-env")
    assert resolve_tenant_id({}) == "tenant-env"


def test_build_artifact_payload_coerces_metadata():
    payload = build_artifact_payload(
        tenant_id="tenant-a",
        artifact_id="doc-1",
        artifact_type="document",
        version="v1",
        artifact_hash="hash",
        metadata={"pack_id": 42},
    )
    assert payload["metadata"]["pack_id"] == "42"
