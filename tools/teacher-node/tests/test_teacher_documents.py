import json
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools" / "teacher-node"))

from documents import FileSystemDocumentStore, build_procedure_document  # noqa: E402
from governance import evaluate_governance  # noqa: E402
from templates import apply_semantic_mapping, load_template  # noqa: E402


def test_document_generation_and_store(tmp_path):
    template_path = ROOT / "contracts" / "fixtures" / "teacher-node" / "teacher_template.sample.json"
    input_path = ROOT / "tools" / "teacher-node" / "tests" / "fixtures" / "document_input.sample.json"

    template = load_template(template_path)
    input_data = json.loads(input_path.read_text(encoding="utf-8"))
    mapped = apply_semantic_mapping(template, input_data)

    governance = evaluate_governance(template, input_data)
    assert governance["requires_approval"] is True
    document = build_procedure_document(template, mapped, input_data, approvals=governance["approvals"])
    assert document["approvals"]

    store = FileSystemDocumentStore(tmp_path)
    result = store.save(document, version="v1")
    assert result["version"] == "v1"

    versions = store.list_versions(document["document_id"])
    assert "v1" in versions

    store.rollback(document["document_id"], "v1")
    loaded = store.get(document["document_id"])
    assert loaded["metadata"]["version"] == "v1"
