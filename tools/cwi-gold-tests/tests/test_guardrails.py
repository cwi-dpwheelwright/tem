import os

import pytest
import requests


pytestmark = pytest.mark.integration


def _auth_headers():
    token = os.environ.get("CWI_AUTH_TOKEN")
    if not token:
        pytest.skip("CWI_AUTH_TOKEN not set")
    return {"Authorization": f"Bearer {token}"}


def _resolve_pack_hash(base_url):
    pack_hash = os.environ.get("CWI_PACK_HASH")
    if pack_hash:
        return pack_hash

    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
    anchor = requests.get(f"{base_url}/packs/{pack_name}/anchor.txt", timeout=10)
    if anchor.status_code != 200:
        pytest.skip("Pack anchor unavailable; set CWI_PACK_HASH to override.")
    return anchor.text.strip()


def _resolve_model_id():
    model_id = os.environ.get("CWI_MODEL_ID") or os.environ.get("MODEL_NAME")
    if not model_id:
        pytest.skip("CWI_MODEL_ID or MODEL_NAME not set")
    return model_id


def _resolve_policy_version():
    return os.environ.get("POLICY_VERSION", "v1")


def _ledger_items_or_fail(base_url, trace_id, context):
    export = requests.get(f"{base_url}/api/ledger/export", params={"trace_id": trace_id}, timeout=20)
    if export.status_code != 200:
        snippet = export.text[:500]
        pytest.fail(
            f"{context}: ledger export failed (status={export.status_code}). "
            f"Response snippet: {snippet!r}. Hint: verify ledger service is reachable."
        )
    try:
        payload = export.json()
    except ValueError:
        snippet = export.text[:500]
        pytest.fail(
            f"{context}: ledger export returned non-JSON response. "
            f"Response snippet: {snippet!r}."
        )
    return payload.get("items", [])


def _guardrails_action_payload(base_url):
    return {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "policy_mode": "approve",
        "pack_hash": _resolve_pack_hash(base_url),
        "model_id": _resolve_model_id(),
        "policy_version": _resolve_policy_version(),
        "action": {
            "type": os.environ.get("CWI_GUARDRAILS_ACTION_TYPE", "risk_demo"),
            "payload": {
                "note": "Combine 10 kg with 5 m in the same instruction."
            }
        }
    }


def _post_guardrails_action(base_url):
    headers = _auth_headers()
    payload = _guardrails_action_payload(base_url)
    resp = requests.post(f"{base_url}/api/action", json=payload, headers=headers, timeout=20)
    assert resp.status_code == 200
    return resp.json()


def test_guardrails_violation_returns_summary(base_url):
    data = _post_guardrails_action(base_url)
    trace = data.get("trace", {})

    violations = trace.get("guardrails", [])
    summary = trace.get("guardrails_summary", {})
    pointers = trace.get("guardrails_pointers", [])

    assert isinstance(violations, list) and violations
    assert summary.get("total", 0) > 0
    assert "high" in (summary.get("severity_counts") or {})
    assert "dimension_compatibility" in [p.lower() for p in pointers]


def test_action_is_gated_by_guardrails(base_url):
    data = _post_guardrails_action(base_url)

    assert data.get("executed") is False

    approval = data.get("approval_ticket")
    result = data.get("result") or {}
    assert approval or result.get("status") == "blocked"


def test_guardrails_validation_event_in_ledger(base_url):
    data = _post_guardrails_action(base_url)
    trace_id = data.get("trace", {}).get("trace_id")
    assert trace_id

    items = _ledger_items_or_fail(base_url, trace_id, "guardrails_validation")
    event_types = [item.get("event_type") for item in items]
    assert "guardrails.validation" in event_types


def test_guardrails_regression_has_summary_and_pointers(base_url):
    data = _post_guardrails_action(base_url)
    trace = data.get("trace", {})
    summary = trace.get("guardrails_summary") or {}
    pointers = trace.get("guardrails_pointers") or []
    assert summary.get("total", 0) > 0
    assert isinstance(pointers, list)
