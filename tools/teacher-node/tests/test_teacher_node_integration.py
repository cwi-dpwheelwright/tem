import os
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[3]

import sys
sys.path.insert(0, str(ROOT / "tools" / "teacher-node"))

from artifact_store import build_artifact_store_from_env


pytestmark = pytest.mark.integration


def test_minio_roundtrip(tmp_path):
    if os.getenv("TEACHER_NODE_MINIO_TEST") != "1":
        pytest.skip("TEACHER_NODE_MINIO_TEST not set")

    store = build_artifact_store_from_env()
    source = tmp_path / "roundtrip.txt"
    source.write_text("ok", encoding="utf-8")

    key = "integration/roundtrip.txt"
    store.upload(source, key)

    dest = tmp_path / "download.txt"
    store.download(key, dest)
    assert dest.read_text(encoding="utf-8") == "ok"
