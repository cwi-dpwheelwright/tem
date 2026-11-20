import os

import pytest
import requests


pytestmark = pytest.mark.integration


def _truthy(value):
    return str(value or "").lower() in ("1", "true", "yes")


def _auth_headers():
    headers = {}
    token = os.environ.get("CWI_AUTH_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    break_glass = os.environ.get("CWI_BREAK_GLASS_TOKEN")
    if break_glass:
        headers["X-Break-Glass-Token"] = break_glass
    trace_token = os.environ.get("CWI_TRACE_QUERY_TOKEN")
    if trace_token:
        headers["X-Trace-Query-Token"] = trace_token
    return headers


def _trace_query_targets(base_url):
    base = os.environ.get("CWI_TRACE_QUERY_BASE_URL")
    router = os.environ.get("CWI_TRACE_QUERY_ROUTER_URL")
    orch = os.environ.get("CWI_TRACE_QUERY_ORCH_URL")

    targets = []
    if base:
        targets.append(base.rstrip("/"))
    if router:
        targets.append(router.rstrip("/"))
    if orch and orch not in targets:
        targets.append(orch.rstrip("/"))

    if not targets:
        targets.append(base_url.rstrip("/"))

    return targets


def _extract_trace_id(payload):
    trace = payload.get("trace") or {}
    trace_id = trace.get("trace_id") or payload.get("trace_id")
    return trace_id


def test_trace_query_spans_emitted_for_ask_action(base_url):
    if not _truthy(os.environ.get("CWI_TRACE_QUERY_ENABLED")):
        pytest.skip("CWI_TRACE_QUERY_ENABLED not set")

    headers = _auth_headers()

    ask_payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "question": "Trace query check.",
        "pack": {"name": os.environ.get("CWI_PACK_NAME", "enterprise")},
        "context": {"mode": "observe", "top_k": 2},
    }

    ask_resp = requests.post(f"{base_url}/api/ask", json=ask_payload, headers=headers, timeout=20)
    assert ask_resp.status_code == 200
    ask_data = ask_resp.json()
    trace_id = _extract_trace_id(ask_data)
    assert trace_id

    for target in _trace_query_targets(base_url):
        trace_resp = requests.get(
            f"{target}/api/dev/trace/{trace_id}/spans",
            headers=headers,
            timeout=10,
        )
        assert trace_resp.status_code == 200
        trace_payload = trace_resp.json()
        spans = trace_payload.get("spans") or []
        assert isinstance(spans, list) and spans

    if not os.environ.get("CWI_AUTH_TOKEN"):
        pytest.skip("CWI_AUTH_TOKEN not set; skipping /api/action span check.")

    action_payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "policy_mode": "approve",
        "action": {"type": "risk_demo", "payload": {"value": "trace-check"}},
    }

    action_resp = requests.post(f"{base_url}/api/action", json=action_payload, headers=headers, timeout=20)
    assert action_resp.status_code == 200
    action_data = action_resp.json()
    action_trace_id = _extract_trace_id(action_data)
    assert action_trace_id

    for target in _trace_query_targets(base_url):
        trace_resp = requests.get(
            f"{target}/api/dev/trace/{action_trace_id}/spans",
            headers=headers,
            timeout=10,
        )
        assert trace_resp.status_code == 200
        trace_payload = trace_resp.json()
        spans = trace_payload.get("spans") or []
        assert isinstance(spans, list) and spans
