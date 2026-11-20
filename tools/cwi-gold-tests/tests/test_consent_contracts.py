import json
from dataclasses import dataclass
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator


pytestmark = [pytest.mark.contract, pytest.mark.consent]

REPO_ROOT = Path(__file__).resolve().parents[3]


@dataclass(frozen=True)
class ContractCase:
    name: str
    schema_path: Path
    fixture_path: Path
    invariant: callable


def _load_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def _validate_schema(schema_path: Path, fixture_path: Path):
    schema = _load_json(schema_path)
    instance = _load_json(fixture_path)
    Draft202012Validator(schema).validate(instance)
    return instance


def _assert_remote_consent(payload):
    assert payload["tenant_id"].strip()
    assert payload["operator_id"].strip()
    consent = payload["consent"]
    assert consent["granted"] is True
    assert consent["granted_utc"].endswith("Z")
    if payload.get("observed_utc"):
        assert payload["observed_utc"].endswith("Z")


CONTRACT_CASES = [
    ContractCase(
        name="remote_consent_response",
        schema_path=REPO_ROOT / "contracts/json-schema/remote_consent_response.json",
        fixture_path=REPO_ROOT / "contracts/fixtures/remote_consent_response.sample.json",
        invariant=_assert_remote_consent,
    ),
]


@pytest.mark.parametrize("case", CONTRACT_CASES, ids=lambda c: c.name)
def test_consent_contracts_schema_and_invariants(case: ContractCase):
    instance = _validate_schema(case.schema_path, case.fixture_path)
    case.invariant(instance)
