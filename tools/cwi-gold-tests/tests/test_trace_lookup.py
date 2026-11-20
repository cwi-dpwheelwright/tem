import os

import pytest
import requests


pytestmark = pytest.mark.integration


def _auth_headers():
    token = os.environ.get("CWI_AUTH_TOKEN")
    if not token:
        pytest.skip("CWI_AUTH_TOKEN not set")
    return {"Authorization": f"Bearer {token}"}


def test_trace_lookup_after_ask(base_url):
    headers = _auth_headers()
    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")

    payload = {
        "tenant_id": "tenant-1",
        "user_id": "trace-test",
        "question": "Find the test document.",
        "pack": {"name": pack_name},
        "context": {"mode": "observe", "top_k": 1},
    }

    resp = requests.post(f"{base_url}/api/ask", json=payload, headers=headers, timeout=20)
    assert resp.status_code == 200
    data = resp.json()
    trace_id = data.get("trace", {}).get("trace_id")
    assert trace_id

    citations = data.get("citations", [])
    if not citations:
        pytest.skip("No citations returned; trace lookup mapping not available.")
    doc_id = citations[0].get("doc_id")
    assert doc_id

    trace_resp = requests.get(f"{base_url}/api/trace/{trace_id}", headers=headers, timeout=20)
    assert trace_resp.status_code == 200
    trace_json = trace_resp.json()
    assert trace_json.get("trace_id") == trace_id
    assert trace_json.get("doc_id") == doc_id


def test_trace_lookup_after_action(base_url):
    headers = _auth_headers()

    payload = {
        "tenant_id": "tenant-1",
        "user_id": "trace-test",
        "policy_mode": "approve",
        "action": {
            "type": "risk_demo",
            "payload": {"value": "test"}
        }
    }

    resp = requests.post(f"{base_url}/api/action", json=payload, headers=headers, timeout=20)
    assert resp.status_code == 200
    data = resp.json()
    trace_id = data.get("trace", {}).get("trace_id")
    assert trace_id

    trace_resp = requests.get(f"{base_url}/api/trace/{trace_id}", headers=headers, timeout=20)
    assert trace_resp.status_code == 200
    trace_json = trace_resp.json()
    assert trace_json.get("trace_id") == trace_id
    assert trace_json.get("doc_id") == "action:risk_demo"
