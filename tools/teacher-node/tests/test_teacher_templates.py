import json
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "tools" / "teacher-node"))

from templates import apply_semantic_mapping, load_template, validate_required_fields, validate_template  # noqa: E402


def test_template_schema_and_mapping():
    template_path = ROOT / "contracts" / "fixtures" / "teacher-node" / "teacher_template.sample.json"
    schema_path = ROOT / "contracts" / "json-schema" / "teacher_template_schema.json"
    input_path = ROOT / "tools" / "teacher-node" / "tests" / "fixtures" / "document_input.sample.json"

    template = load_template(template_path)
    validate_template(template, schema_path)

    input_data = json.loads(input_path.read_text(encoding="utf-8"))
    mapped = apply_semantic_mapping(template, input_data)

    missing = validate_required_fields(template, mapped)
    assert not missing
    assert mapped["purpose"].startswith("Validate entry protocol")
    assert "Verify perimeter clearance." in mapped["steps"]
