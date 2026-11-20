import json
import re
from pathlib import Path

import pytest


pytestmark = pytest.mark.contract

REPO_ROOT = Path(__file__).resolve().parents[3]

REQUIRED_TRACE_FIELDS = {"sources", "confidence", "policy_mode", "model", "pack_hash"}
HARD_GATES = {
    "pack_trust",
    "frozen_trace_contract",
    "approval_gating",
    "provenance_ledger",
    "kill_switch",
    "tenant_isolation",
    "privacy_opsec",
    "rollback",
}
RECURSIVE_LOOP_STEPS = [
    "controlled_input_event",
    "process_miner_observation",
    "normalized_event",
    "memory_graph_candidate",
    "policy_decision",
    "graph_update",
    "vector_refresh",
    "retrieval_improvement",
    "answer_improvement",
    "critic_validation",
    "truth_engine_state",
    "ledger_audit_proof",
    "regression_quality_update",
    "repeatability_check",
]
CONTROLLED_ASK_CATEGORIES = {
    "ownership",
    "dependency",
    "open_loop",
    "reinforcement",
    "conflict",
    "cache_freshness",
    "privacy_boundary",
}
IMMUTABLE_CONTRACT_EVIDENCE = {
    "pack_hash",
    "anchor_hash",
    "canonical_pack_hash",
    "pack_schema_version",
    "trace_contract_version",
    "recursive_event_schema_version",
    "policy_bundle_version",
    "approval_policy_version",
    "ledger_hash_suite_id",
    "fixture_ids",
    "gold_test_ids",
    "migration_versions",
}


def _load_json(relative_path: str):
    return json.loads((REPO_ROOT / relative_path).read_text(encoding="utf-8"))


def _load_text(relative_path: str):
    return (REPO_ROOT / relative_path).read_text(encoding="utf-8")


def _component_section(openapi_text: str, component_name: str) -> str:
    marker = f"    {component_name}:\n"
    start = openapi_text.index(marker)
    rest = openapi_text[start + len(marker) :]
    next_component = re.search(r"\n    [A-Za-z0-9_]+:\n", rest)
    end = len(rest) if next_component is None else next_component.start()
    return rest[:end]


def _required_items(component_text: str) -> set[str]:
    lines = component_text.splitlines()
    items: set[str] = set()
    in_required = False
    for line in lines:
        if line.strip() == "required:":
            in_required = True
            continue
        if not in_required:
            continue
        if line.startswith("      - "):
            items.add(line.strip()[2:].strip())
            continue
        if line.strip():
            break
    return items


@pytest.mark.parametrize(
    "relative_path",
    [
        "contracts/openapi/cwi-router.openapi_10H_frozen.yaml",
        "contracts/openapi/cwi-orchestrator.openapi_10H_frozen.yaml",
    ],
)
def test_frozen_response_trace_contract_is_declared(relative_path):
    openapi_text = _load_text(relative_path)

    trace = _component_section(openapi_text, "Trace")
    ask_response = _component_section(openapi_text, "AskResponse")
    action_response = _component_section(openapi_text, "ActionResponse")

    assert REQUIRED_TRACE_FIELDS.issubset(_required_items(trace))
    assert "trace" in _required_items(ask_response)
    assert "trace" in _required_items(action_response)
    assert "$ref: '#/components/schemas/Trace'" in ask_response
    assert "$ref: '#/components/schemas/Trace'" in action_response
    assert "$ref: '#/components/schemas/ApprovalTicket'" in action_response


@pytest.mark.parametrize(
    "relative_path",
    [
        "contracts/fixtures/orchestrator/ask-response.json",
        "contracts/fixtures/orchestrator/action-response.json",
        "contracts/fixtures/orchestrator/trace-governance.json",
    ],
)
def test_response_fixtures_preserve_required_trace_fields(relative_path):
    payload = _load_json(relative_path)
    trace = payload.get("trace", payload)

    assert REQUIRED_TRACE_FIELDS.issubset(trace)
    assert isinstance(trace["sources"], list)
    assert isinstance(trace["confidence"], (int, float))
    assert 0 <= trace["confidence"] <= 1
    assert isinstance(trace["policy_mode"], str)
    assert isinstance(trace["model"], str)
    assert isinstance(trace["pack_hash"], str)


def test_action_fixture_preserves_approval_ticket_for_gated_action():
    payload = _load_json("contracts/fixtures/orchestrator/action-response.json")

    assert payload["executed"] is False
    assert payload["approval_ticket"]
    assert payload["approval_ticket"]["ticket_id"]
    assert payload["approval_ticket"]["risk"] in {"medium", "high", "critical"}
    assert payload["approval_ticket"]["reason"]


def test_recursive_validation_run_schema_requires_hard_gate_evidence():
    schema = _load_json("contracts/json-schema/recursive_validation_run.json")
    contract_evidence = schema["properties"]["contract_evidence"]
    hard_gate_enum = (
        schema["$defs"]["hard_gate"]["properties"]["gate_id"]["enum"]
    )

    assert set(schema["required"]) >= {
        "contract_evidence",
        "recursive_loop_steps",
        "hard_gates",
        "quality_gates",
        "trace_ids",
        "ledger_refs",
        "results",
    }
    assert set(contract_evidence["required"]) == IMMUTABLE_CONTRACT_EVIDENCE
    assert set(hard_gate_enum) == HARD_GATES
    assert schema["$defs"]["hard_gate"]["properties"]["zero_tolerance"]["const"] is True


def test_recursive_validation_run_fixture_records_zero_tolerance_gates():
    payload = _load_json("contracts/fixtures/orchestrator/recursive-validation-run.sample.json")
    hard_gates = {gate["gate_id"]: gate for gate in payload["hard_gates"]}

    assert set(hard_gates) == HARD_GATES
    for gate in hard_gates.values():
        assert gate["zero_tolerance"] is True
        assert gate["status"] == "PASS"
        assert gate["evidence_refs"]

    contract_evidence = payload["contract_evidence"]
    assert contract_evidence["pack_hash"] == contract_evidence["anchor_hash"]
    assert contract_evidence["canonical_pack_hash"] == contract_evidence["anchor_hash"]
    assert payload["results"]["critical_failures"] == 0
    assert payload["results"]["privacy_failures"] == 0
    assert payload["results"]["cross_tenant_leakage"] == 0


def test_recursive_validation_run_fixture_covers_full_loop_order():
    payload = _load_json("contracts/fixtures/orchestrator/recursive-validation-run.sample.json")
    steps = [step["step_id"] for step in payload["recursive_loop_steps"]]

    assert steps == RECURSIVE_LOOP_STEPS
    for step in payload["recursive_loop_steps"]:
        assert step["status"] == "PASS"
        assert step["evidence_refs"]


def test_anchor_rules_keep_canonical_hash_as_pack_trust_source():
    rules = _load_text("ANCHOR_RULES.md").lower()

    assert "canonicalized json" in rules
    assert "compute sha-256" in rules
    assert "anchor.txt" in rules


def test_controlled_ask_suite_has_50_unique_traceable_cases():
    suite = _load_json("testing/recursive-intelligence/controlled-ask-e2e-suite.v1.json")
    cases = suite["cases"]
    case_ids = [case["case_id"] for case in cases]
    entity_tokens = [case["entity_token"] for case in cases]

    assert suite["schema_version"] == "cwi.recursive_controlled_ask_suite.v1"
    assert len(cases) == 50
    assert len(set(case_ids)) == 50
    assert len(set(entity_tokens)) == 50
    assert {case["category"] for case in cases} == CONTROLLED_ASK_CATEGORIES
    assert set(suite["default_assertions"]["hard_gates"]) == HARD_GATES


def test_controlled_ask_cases_are_non_sensitive_and_assertable():
    suite = _load_json("testing/recursive-intelligence/controlled-ask-e2e-suite.v1.json")

    for case in suite["cases"]:
        assert case["entity_token"].startswith("CWI-RL-")
        assert case["entity_token"] in case["baseline_prompt"]
        assert case["learning_events"]
        assert case["post_learning_prompts"]
        assert case["expected_terms_all"]
        assert case["forbidden_terms"]
        assert case["quality_focus"]
        for event in case["learning_events"]:
            assert case["entity_token"] in event
        for prompt in case["post_learning_prompts"]:
            assert case["entity_token"] in prompt

    privacy_cases = [case for case in suite["cases"] if case["category"] == "privacy_boundary"]
    assert len(privacy_cases) == 5
    for case in privacy_cases:
        canary = f"PRIVATE-CANARY-{case['entity_token'].removeprefix('CWI-')}"
        assert canary in " ".join(case["learning_events"])
        assert canary in case["forbidden_terms"]
