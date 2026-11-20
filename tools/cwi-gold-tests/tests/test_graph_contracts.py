import json
from dataclasses import dataclass
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator


pytestmark = [pytest.mark.contract, pytest.mark.graph]

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


def _assert_graph_node(payload):
    assert payload["node_id"].strip()
    assert payload["labels"]
    assert payload["source"].strip()
    assert payload["created_utc"].endswith("Z")


def _assert_graph_edge(payload):
    assert payload["edge_id"].strip()
    assert payload["relation"].strip()
    assert payload["from_node_id"].strip()
    assert payload["to_node_id"].strip()
    assert payload["created_utc"].endswith("Z")


CONTRACT_CASES = [
    ContractCase(
        name="graph_node",
        schema_path=REPO_ROOT / "contracts/json-schema/graph_node.json",
        fixture_path=REPO_ROOT / "contracts/fixtures/graph/graph_node.sample.json",
        invariant=_assert_graph_node,
    ),
    ContractCase(
        name="graph_edge",
        schema_path=REPO_ROOT / "contracts/json-schema/graph_edge.json",
        fixture_path=REPO_ROOT / "contracts/fixtures/graph/graph_edge.sample.json",
        invariant=_assert_graph_edge,
    ),
]


@pytest.mark.parametrize("case", CONTRACT_CASES, ids=lambda c: c.name)
def test_graph_contracts_schema_and_invariants(case: ContractCase):
    instance = _validate_schema(case.schema_path, case.fixture_path)
    case.invariant(instance)
