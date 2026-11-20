import json
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools" / "teacher-node"))

from reconciliation import write_reconciliation_bundle  # noqa: E402


def test_reconciliation_bundle(tmp_path):
    input_path = ROOT / "tools" / "teacher-node" / "tests" / "fixtures" / "reconciliation_input.sample.json"
    input_data = json.loads(input_path.read_text(encoding="utf-8"))

    paths = write_reconciliation_bundle(input_data, tmp_path)

    report = json.loads(paths["report_json"].read_text(encoding="utf-8"))
    assert report["report_type"] == "reconciliation"
    assert report["structured"]["conflict_count"] == 3

    if paths.get("discrepancies"):
        lines = paths["discrepancies"].read_text(encoding="utf-8").strip().splitlines()
        assert lines
