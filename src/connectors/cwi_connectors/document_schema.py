from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict

import jsonschema


def _find_schema() -> Path:
    current = Path(__file__).resolve()
    for _ in range(8):
        candidate = current / "contracts" / "json-schema" / "document_record.json"
        if candidate.exists():
            return candidate
        current = current.parent
    raise FileNotFoundError("Unable to locate contracts/json-schema/document_record.json")


def validate_document(record: Dict[str, Any]) -> None:
    schema = json.loads(_find_schema().read_text(encoding="utf-8"))
    validator = jsonschema.Draft202012Validator(schema, format_checker=jsonschema.FormatChecker())
    errors = sorted(validator.iter_errors(record), key=lambda e: e.path)
    if errors:
        message = "; ".join(error.message for error in errors)
        raise ValueError(f"Document record invalid: {message}")
