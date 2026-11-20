import json
from dataclasses import dataclass
from pathlib import Path

import pytest
from jsonschema import Draft202012Validator


pytestmark = [pytest.mark.contract, pytest.mark.patent]

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


def _assert_operator_memory_correlation(payload):
    assert payload["correlation_score"] >= 0
    assert payload["correlation_score"] <= 1
    assert payload["signals"]
    assert payload["performance"]
    assert payload["consent"]["granted"] is True
    assert payload["consent"]["granted_utc"].endswith("Z")


def _assert_ownership_succession_plan(payload):
    successors = payload["successors"]
    assert successors
    orders = [entry["order"] for entry in successors]
    assert orders == sorted(orders)
    assert len(set(orders)) == len(orders)
    assert payload["status"]


def _assert_procedure_document(payload):
    assert payload["title"].strip()
    assert payload["content"].strip()
    assert payload["status"]
    assert payload["generated_utc"].endswith("Z")


def _assert_mesh_placement_recommendation(payload):
    assert payload["nodes"]
    for node in payload["nodes"]:
        location = node["recommended_location"]
        assert -90 <= location["lat"] <= 90
        assert -180 <= location["lon"] <= 180
        assert 0 <= node["confidence"] <= 1


def _assert_dual_format_report(payload):
    assert payload["narrative"].strip()
    assert isinstance(payload["structured"], dict)
    assert payload["structured"]
    assert 0 <= payload["coherence_score"] <= 1
    assert payload["conflict_count"] >= 0


def _assert_inference_discrepancy_event(payload):
    sources = payload["sources"]
    discrepancies = payload["discrepancies"]
    assert len(sources) >= 2
    assert discrepancies
    assert payload["severity"] in {"low", "medium", "high", "critical"}


def _assert_provenance_envelope(payload):
    assert payload.get("version")
    assert payload.get("envelope_id")
    assert payload.get("emitted_utc")
    assert payload.get("connector_id")
    assert payload.get("source")
    summary = payload.get("summary") or {}
    assert isinstance(summary.get("doc_ids"), list)
    assert isinstance(summary.get("chunk_ids"), list)
    assert isinstance(summary.get("redaction_count"), int)


def _assert_route_attestation_envelope(payload):
    assert payload.get("schema_version")
    assert payload.get("attestation_id")
    assert payload.get("issued_utc", "").endswith("Z")
    assert payload.get("trace_id")
    assert payload.get("decision_id")
    assert payload.get("selected_tier") in {"pro", "free", "edge"}
    if "network_score" in payload:
        assert 0 <= payload["network_score"] <= 1
    if "sequence" in payload:
        assert payload["sequence"] >= 1


CONTRACT_CASES = [
    ContractCase(
        name="patent8_operator_memory_correlation",
        schema_path=REPO_ROOT / "contracts/json-schema/operator_memory_correlation.json",
        fixture_path=REPO_ROOT / "contracts/fixtures/operator_memory_correlation.sample.json",
        invariant=_assert_operator_memory_correlation,
    ),
    ContractCase(
        name="patent10_ownership_succession_plan",
        schema_path=REPO_ROOT / "contracts/json-schema/ownership_succession_plan.json",
        fixture_path=REPO_ROOT / "contracts/fixtures/ownership_succession_plan.sample.json",
        invariant=_assert_ownership_succession_plan,
    ),
    ContractCase(
        name="patent11_procedure_document",
        schema_path=REPO_ROOT / "contracts/json-schema/procedure_document.json",
        fixture_path=REPO_ROOT / "contracts/fixtures/procedure_document.sample.json",
        invariant=_assert_procedure_document,
    ),
    ContractCase(
        name="patent12_mesh_placement_recommendation",
        schema_path=REPO_ROOT / "contracts/json-schema/mesh_placement_recommendation.json",
        fixture_path=REPO_ROOT / "contracts/fixtures/mesh_placement_recommendation.sample.json",
        invariant=_assert_mesh_placement_recommendation,
    ),
    ContractCase(
        name="patent13_dual_format_report",
        schema_path=REPO_ROOT / "contracts/json-schema/dual_format_report.json",
        fixture_path=REPO_ROOT / "contracts/fixtures/dual_format_report.sample.json",
        invariant=_assert_dual_format_report,
    ),
    ContractCase(
        name="patent14_inference_discrepancy_event",
        schema_path=REPO_ROOT / "contracts/json-schema/inference_discrepancy_event.json",
        fixture_path=REPO_ROOT / "contracts/fixtures/inference_discrepancy_event.sample.json",
        invariant=_assert_inference_discrepancy_event,
    ),
    ContractCase(
        name="provenance_envelope",
        schema_path=REPO_ROOT / "contracts/json-schema/provenance_envelope.json",
        fixture_path=REPO_ROOT / "contracts/fixtures/provenance_envelope.sample.json",
        invariant=_assert_provenance_envelope,
    ),
    ContractCase(
        name="patent1_route_attestation_envelope",
        schema_path=REPO_ROOT / "contracts/json-schema/route_attestation_envelope.json",
        fixture_path=REPO_ROOT / "contracts/fixtures/router/route-attestation-envelope.sample.json",
        invariant=_assert_route_attestation_envelope,
    ),
]


@pytest.mark.parametrize("case", CONTRACT_CASES, ids=lambda c: c.name)
def test_patent_contracts_schema_and_invariants(case: ContractCase):
    instance = _validate_schema(case.schema_path, case.fixture_path)
    case.invariant(instance)
