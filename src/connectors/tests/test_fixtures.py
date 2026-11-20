import json
from pathlib import Path

from cwi_connectors.document_schema import validate_document


def _repo_root() -> Path:
    current = Path(__file__).resolve()
    for parent in current.parents:
        if (parent / "contracts").exists():
            return parent
    return current.parents[3]


def _load(path: str):
    return json.loads((_repo_root() / path).read_text(encoding="utf-8"))


def test_drive_fixture_valid():
    payload = _load("contracts/fixtures/drive/list_files.json")
    validate_document(payload["documents"][0])


def test_outlook_fixture_valid():
    payload = _load("contracts/fixtures/outlook/messages.json")
    validate_document(payload["documents"][0])


def test_smartsheet_fixture_valid():
    payload = _load("contracts/fixtures/smartsheet/sheet.json")
    validate_document(payload["documents"][0])
