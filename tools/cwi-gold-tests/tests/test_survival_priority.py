import os
import time

import pytest
import requests


pytestmark = pytest.mark.integration


def _truthy(value):
    return str(value or "").lower() in ("1", "true", "yes")


def _headers():
    headers = {}
    token = os.environ.get("CWI_AUTH_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    return headers


def _wait_for_survival(base_url, headers, timeout_seconds=20):
    deadline = time.time() + timeout_seconds
    while time.time() < deadline:
        resp = requests.get(f"{base_url}/api/survival/status", headers=headers, timeout=10)
        if resp.status_code == 200:
            mode = (resp.json() or {}).get("mode")
            if mode in ("survival", "degraded"):
                return mode
        time.sleep(2)
    return None


def test_survival_priority_gates_low_priority_requests(base_url):
    if not _truthy(os.environ.get("CWI_SURVIVAL_PRIORITY_TEST")):
        pytest.skip("CWI_SURVIVAL_PRIORITY_TEST not set")

    headers = _headers()
    if "Authorization" not in headers:
        pytest.skip("CWI_AUTH_TOKEN not set")

    signal = {
        "signal_type": "priority_test",
        "severity": 0.95,
        "details": "force survival mode",
    }
    resp = requests.post(f"{base_url}/api/survival/signal", json=signal, headers=headers, timeout=10)
    assert resp.status_code == 200

    mode = _wait_for_survival(base_url, headers)
    assert mode in ("survival", "degraded")

    ask_payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "question": "Priority check",
        "pack": {"name": os.environ.get("CWI_PACK_NAME", "enterprise")},
        "context": {"mode": "observe", "top_k": 2},
    }

    low_headers = dict(headers)
    low_headers["x-priority"] = "low"
    blocked = None
    for _ in range(5):
        blocked = requests.post(f"{base_url}/api/ask", json=ask_payload, headers=low_headers, timeout=20)
        if blocked.status_code == 503:
            break
        time.sleep(2)
    assert blocked is not None
    assert blocked.status_code == 503

    high_headers = dict(headers)
    high_headers["x-priority"] = "high"
    allowed = requests.post(f"{base_url}/api/ask", json=ask_payload, headers=high_headers, timeout=20)
    assert allowed.status_code == 200
