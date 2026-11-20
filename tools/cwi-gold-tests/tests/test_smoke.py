import hashlib
import json
import os

import pytest
import requests

pytestmark = pytest.mark.smoke


def canonicalize_jcs(data):
    # RFC 8785 (JSON Canonicalization Scheme) style serialization
    return json.dumps(data, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def _auth_headers():
    token = os.environ.get("CWI_AUTH_TOKEN")
    if not token:
        return {}
    return {"Authorization": f"Bearer {token}"}


def test_pack_anchor_smoke(base_url):
    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
    pack_url = f"{base_url}/packs/{pack_name}/pack.json"
    anchor_url = f"{base_url}/packs/{pack_name}/anchor.txt"

    pack_resp = requests.get(pack_url, timeout=10)
    anchor_resp = requests.get(anchor_url, timeout=10)

    assert pack_resp.status_code == 200
    assert anchor_resp.status_code == 200

    pack_json = pack_resp.json()
    canonical = canonicalize_jcs(pack_json).encode("utf-8")
    digest = hashlib.sha256(canonical).hexdigest()
    anchor = anchor_resp.text.strip()

    assert digest == anchor


def test_ask_trace_invariants(base_url):
    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
    payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "question": "What is the system status?",
        "pack": {"name": pack_name},
        "context": {"mode": "observe", "top_k": 3},
    }

    headers = {"x-tenant-id": "tenant-1"}
    headers.update(_auth_headers())
    resp = requests.post(f"{base_url}/api/ask", json=payload, headers=headers, timeout=20)
    assert resp.status_code == 200

    data = resp.json()
    trace = data.get("trace", {})
    assert "trace_id" in trace
    assert "sources" in trace
    assert "confidence" in trace
    assert "policy_mode" in trace
    assert "model" in trace
    assert "pack_hash" in trace


def test_action_trace_invariants(base_url):
    headers = {"x-tenant-id": "tenant-1"}
    headers.update(_auth_headers())
    if not headers.get("Authorization"):
        pytest.skip("CWI_AUTH_TOKEN not set")

    payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "policy_mode": "approve",
        "action": {
            "type": os.environ.get("CWI_SMOKE_ACTION_TYPE", "risk_demo"),
            "payload": {"value": "smoke-test"},
        },
    }

    resp = requests.post(f"{base_url}/api/action", json=payload, headers=headers, timeout=20)
    assert resp.status_code == 200

    data = resp.json()
    trace = data.get("trace", {})
    assert "trace_id" in trace
    assert "sources" in trace
    assert "confidence" in trace
    assert "policy_mode" in trace
    assert "model" in trace


def test_gateway_headers_and_health(base_url):
    resp = requests.get(f"{base_url}/healthz", timeout=10)
    assert resp.status_code == 200
    _assert_security_headers(resp)

    ready = requests.get(f"{base_url}/readyz", timeout=10)
    assert ready.status_code == 200
    _assert_security_headers(ready)


def test_gateway_artifacts_route(base_url):
    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
    headers = {"x-tenant-id": "tenant-1"}
    headers.update(_auth_headers())
    resp = requests.get(
        f"{base_url}/artifacts/reports?tenant_id=tenant-1&pack={pack_name}",
        headers=headers,
        timeout=10
    )

    if resp.status_code == 404:
        fallback = requests.get(
            f"{base_url}/api/reports?tenant_id=tenant-1",
            headers=headers,
            timeout=10
        )
        assert fallback.status_code != 404
        _assert_security_headers(fallback)
        return

    assert resp.status_code != 404
    _assert_security_headers(resp)


def test_pack_service_artifact_indexes(base_url):
    packs_base = os.environ.get("CWI_PACKS_BASE_URL", base_url).rstrip("/")
    reports = requests.get(f"{packs_base}/artifacts/reports/index.json", timeout=10)
    assert reports.status_code == 200
    _assert_security_headers(reports)

    docs = requests.get(f"{packs_base}/artifacts/docs/index.json", timeout=10)
    assert docs.status_code == 200
    _assert_security_headers(docs)


def _assert_security_headers(response):
    headers = response.headers
    assert headers.get("X-Content-Type-Options") == "nosniff"
    assert headers.get("Referrer-Policy") == "no-referrer"
    assert headers.get("Content-Security-Policy") == "default-src 'none'; frame-ancestors 'none'; base-uri 'none'"
    assert headers.get("Permissions-Policy") == "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()"
    assert headers.get("X-Frame-Options") == "DENY"
    assert "X-Request-Id" in headers
    assert "Server" not in headers
    assert "X-Powered-By" not in headers
