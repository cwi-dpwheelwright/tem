from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict
from urllib.parse import urljoin

import jsonschema
from referencing import Registry, Resource


def _find_schema() -> Path:
    current = Path(__file__).resolve()
    for _ in range(8):
        candidate = current / "contracts" / "json-schema" / "canonical_record_upsert.json"
        if candidate.exists():
            return candidate
        current = current.parent
    raise FileNotFoundError("Unable to locate contracts/json-schema/canonical_record_upsert.json")


def validate_canonical_record(record: Dict[str, Any]) -> None:
    schema_path = _find_schema()
    schema = json.loads(schema_path.read_text(encoding="utf-8"))
    source_path = schema_path.parent / "source_metadata.json"
    source_schema = json.loads(source_path.read_text(encoding="utf-8"))
    source_id = source_schema.get("$id", "")
    schema_id = schema.get("$id", "")
    resolved_url = urljoin(schema_id, "contracts/json-schema/source_metadata.json") if schema_id else ""
    registry = Registry()
    for uri in {
        source_id,
        "contracts/json-schema/source_metadata.json",
        resolved_url,
    }:
        if uri:
            registry = registry.with_resource(uri, Resource.from_contents(source_schema))
    validator = jsonschema.Draft202012Validator(
        schema,
        format_checker=jsonschema.FormatChecker(),
        registry=registry,
    )
    errors = sorted(validator.iter_errors(record), key=lambda e: e.path)
    if errors:
        message = "; ".join(error.message for error in errors)
        raise ValueError(f"Canonical record invalid: {message}")
