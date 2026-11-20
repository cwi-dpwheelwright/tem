import json
import os

import pytest
import requests


pytestmark = [pytest.mark.integration, pytest.mark.patent]


def _auth_headers():
    token = os.environ.get("CWI_AUTH_TOKEN")
    headers = {}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    break_glass = os.environ.get("CWI_BREAK_GLASS_TOKEN")
    if break_glass:
        headers["X-Break-Glass-Token"] = break_glass
    return headers


def _load_payload(env_key):
    value = os.environ.get(env_key)
    if not value:
        return None
    try:
        payload = json.loads(value)
        return payload
    except json.JSONDecodeError:
        pass

    if os.path.exists(value):
        with open(value, "r", encoding="utf-8") as handle:
            return json.loads(handle.read())
    return None


def test_discrepancy_detection_semantic_alignment(base_url):
    payload = _load_payload("CWI_DISCREPANCY_ASK_PAYLOAD_JSON")
    if payload is None:
        pytest.skip("CWI_DISCREPANCY_ASK_PAYLOAD_JSON not set")

    resp = requests.post(f"{base_url}/api/ask", json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code == 200
    data = resp.json()
    trace = data.get("trace", {})

    assert trace.get("discrepancy_score") is not None or trace.get("discrepancy_severity")
    discrepancies = trace.get("discrepancies") or []
    assert isinstance(discrepancies, list) and discrepancies


def test_discrepancy_correction_fallback_and_approval(base_url):
    payload = _load_payload("CWI_DISCREPANCY_FALLBACK_PAYLOAD_JSON")
    if payload is None:
        pytest.skip("CWI_DISCREPANCY_FALLBACK_PAYLOAD_JSON not set")

    resp = requests.post(f"{base_url}/api/ask", json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code == 200
    data = resp.json()
    trace = data.get("trace", {})

    assert trace.get("fallback_applied") is True or (trace.get("corrections") or [])

    if os.environ.get("CWI_DISCREPANCY_EXPECT_APPROVAL", "").lower() in ("1", "true", "yes"):
        assert trace.get("discrepancy_requires_approval") is True
        assert trace.get("discrepancy_ticket_id")


def test_discrepancy_triggers_approval_for_risky_action(base_url):
    payload = _load_payload("CWI_DISCREPANCY_ACTION_PAYLOAD_JSON")
    if payload is None:
        pytest.skip("CWI_DISCREPANCY_ACTION_PAYLOAD_JSON not set")

    resp = requests.post(f"{base_url}/api/action", json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code == 200
    data = resp.json()
    assert data.get("executed") is False
    approval = data.get("approval_ticket") or {}
    assert approval.get("ticket_id")


def test_validator_failure_yields_governed_response(base_url):
    payload = _load_payload("CWI_VALIDATOR_FAILURE_ACTION_PAYLOAD_JSON")
    if payload is None:
        pytest.skip("CWI_VALIDATOR_FAILURE_ACTION_PAYLOAD_JSON not set")

    resp = requests.post(f"{base_url}/api/action", json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code == 200
    data = resp.json()
    assert data.get("executed") is False
    assert data.get("approval_ticket") or (data.get("result") or {}).get("status") == "blocked"
