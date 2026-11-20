import datetime
import os

import pytest
import requests


pytestmark = pytest.mark.e2e


def _truthy(value):
    return str(value or "").lower() in ("1", "true", "yes")


def _auth_headers():
    token = os.environ.get("CWI_AUTH_TOKEN")
    if not token:
        return None
    return {"Authorization": f"Bearer {token}"}


def _telemetry_payload():
    now = datetime.datetime.now(tz=datetime.timezone.utc)
    start = now - datetime.timedelta(minutes=5)
    return {
        "tenant_id": os.environ.get("CWI_TENANT_ID", "tenant-1"),
        "window": {
            "start": start.isoformat(),
            "end": now.isoformat(),
        },
        "signals": [
            {"tag": "latency_ms", "unit": "ms", "series": [150.0, 120.0, 180.0]},
            {"tag": "cpu_util", "unit": "%", "series": [0.4, 0.6, 0.5]},
        ],
        "metadata": {"source": "gold-tests-e2e"},
    }


def _ledger_export(base_url, trace_id, headers):
    resp = requests.get(
        f"{base_url}/api/ledger/export",
        params={"trace_id": trace_id},
        headers=headers,
        timeout=20,
    )
    assert resp.status_code == 200
    payload = resp.json()
    assert isinstance(payload.get("items"), list)
    return payload


def test_backend_full_stack_flows(base_url):
    if not _truthy(os.environ.get("CWI_E2E_BACKEND_FLOWS")):
        pytest.skip("CWI_E2E_BACKEND_FLOWS not set")

    headers = _auth_headers()
    if headers is None:
        pytest.skip("CWI_AUTH_TOKEN not set")

    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")

    ask_payload = {
        "tenant_id": os.environ.get("CWI_TENANT_ID", "tenant-1"),
        "user_id": "user-1",
        "question": "E2E: system status",
        "pack": {"name": pack_name},
        "context": {"mode": "observe", "top_k": 2},
    }
    ask = requests.post(f"{base_url}/api/ask", json=ask_payload, headers=headers, timeout=30)
    assert ask.status_code == 200
    ask_data = ask.json()
    ask_trace = ask_data.get("trace", {})
    assert ask_trace.get("trace_id")

    if _truthy(os.environ.get("CWI_E2E_BACKEND_LEDGER", "1")):
        _ledger_export(base_url, ask_trace.get("trace_id"), headers)

    action_payload = {
        "tenant_id": os.environ.get("CWI_TENANT_ID", "tenant-1"),
        "user_id": "user-1",
        "policy_mode": "approve",
        "action": {
            "type": os.environ.get("CWI_E2E_ACTION_TYPE", "risk_demo"),
            "payload": {"value": "e2e"},
        },
    }
    action = requests.post(f"{base_url}/api/action", json=action_payload, headers=headers, timeout=30)
    assert action.status_code == 200
    action_data = action.json()
    action_trace = action_data.get("trace", {})
    assert action_trace.get("trace_id")

    if _truthy(os.environ.get("CWI_E2E_BACKEND_LEDGER", "1")):
        _ledger_export(base_url, action_trace.get("trace_id"), headers)

    telemetry = requests.post(
        f"{base_url}/api/ingest/telemetry",
        json=_telemetry_payload(),
        headers=headers,
        timeout=30,
    )
    assert telemetry.status_code == 200
    telemetry_data = telemetry.json()
    assert telemetry_data.get("ingest_id")
    assert telemetry_data.get("trace", {}).get("trace_id")
