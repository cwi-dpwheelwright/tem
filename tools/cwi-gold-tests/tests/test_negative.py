import hashlib
import json
import os
import requests
import pytest


pytestmark = pytest.mark.negative


def _auth_headers():
    token = os.environ.get("CWI_AUTH_TOKEN")
    if not token:
        return None
    return {"Authorization": f"Bearer {token}"}

def _db_down_enabled():
    return os.environ.get("CWI_EXPECT_DB_DOWN", "").lower() in ("1", "true", "yes")


def canonicalize_json(data):
    return json.dumps(data, sort_keys=True, separators=(",", ":"), ensure_ascii=False)

def _assert_error_contract(resp):
    content_type = resp.headers.get("Content-Type", "")
    if not content_type.startswith("application/json") or not resp.content:
        return

    payload = resp.json()
    if not isinstance(payload, dict):
        return

    is_service_response = "isSuccess" in payload or "ok" in payload
    if is_service_response:
        if "isSuccess" in payload:
            assert payload.get("isSuccess") is False
        if "ok" in payload:
            assert payload.get("ok") is False
        status_code = payload.get("statusCode") or payload.get("status_code")
        if status_code is not None:
            assert int(status_code) == resp.status_code
        if "message" in payload:
            assert payload["message"], "message field must be non-empty"
        if "data" in payload:
            assert payload["data"] is None
    else:
        if "error" in payload:
            assert payload["error"], "error field must be non-empty"
        if "message" in payload:
            assert payload["message"], "message field must be non-empty"
        if "error" not in payload and "message" not in payload:
            assert payload.get("code"), "expected error or code field"

    serialized = json.dumps(payload).lower()
    assert "stack" not in serialized
    assert "exception" not in serialized
    assert "traceback" not in serialized


def _reverse_keys(value):
    if isinstance(value, dict):
        return {key: _reverse_keys(value[key]) for key in sorted(value.keys(), reverse=True)}
    if isinstance(value, list):
        return [_reverse_keys(item) for item in value]
    return value


def test_invalid_ask_returns_400(base_url):
    if _db_down_enabled():
        pytest.skip("Skipping ask schema validation while DB is down")

    payload = {"foo": "bar"}
    headers = _auth_headers()
    resp = requests.post(f"{base_url}/api/ask", json=payload, headers=headers, timeout=10)
    assert resp.status_code == 400
    _assert_error_contract(resp)


def test_missing_jwt_returns_401(base_url):
    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
    payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "question": "What is the system status?",
        "pack": {"name": pack_name},
        "context": {"mode": "observe", "top_k": 3},
    }

    resp = requests.post(f"{base_url}/api/ask", json=payload, timeout=10)
    assert resp.status_code == 401
    _assert_error_contract(resp)


def test_invalid_jwt_returns_401(base_url):
    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
    payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "question": "What is the system status?",
        "pack": {"name": pack_name},
        "context": {"mode": "observe", "top_k": 3},
    }

    headers = {"Authorization": "Bearer invalid.token.value"}
    resp = requests.post(f"{base_url}/api/ask", json=payload, headers=headers, timeout=10)
    assert resp.status_code in (401, 403)
    _assert_error_contract(resp)


def test_pack_tamper_detects_anchor_mismatch(base_url):
    packs_base_url = os.environ.get("CWI_PACKS_BASE_URL", base_url).rstrip("/")
    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
    pack_url = f"{packs_base_url}/packs/{pack_name}/pack.json"
    anchor_url = f"{packs_base_url}/packs/{pack_name}/anchor.txt"

    pack_resp = requests.get(pack_url, timeout=10)
    anchor_resp = requests.get(anchor_url, timeout=10)

    assert pack_resp.status_code == 200
    assert anchor_resp.status_code == 200

    pack_json = pack_resp.json()
    pack_json["tampered"] = True

    digest = hashlib.sha256(canonicalize_json(pack_json).encode("utf-8")).hexdigest()
    anchor = anchor_resp.text.strip()

    assert digest != anchor


def test_pack_anchor_mismatch_rejected(base_url):
    headers = _auth_headers()
    if headers is None:
        pytest.skip("CWI_AUTH_TOKEN not set")

    packs_base_url = os.environ.get("CWI_PACKS_BASE_URL", base_url).rstrip("/")
    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
    pack_url = f"{packs_base_url}/packs/{pack_name}/pack.json"
    anchor_url = f"{packs_base_url}/packs/{pack_name}/anchor.txt"

    pack_resp = requests.get(pack_url, timeout=10)
    anchor_resp = requests.get(anchor_url, timeout=10)

    assert pack_resp.status_code == 200
    assert anchor_resp.status_code == 200

    pack_json = pack_resp.json()
    reversed_pack = _reverse_keys(pack_json)
    mismatch_anchor = hashlib.sha256(canonicalize_json(reversed_pack).encode("utf-8")).hexdigest()
    if mismatch_anchor == anchor_resp.text.strip():
        reversed_pack["tampered"] = True
        mismatch_anchor = hashlib.sha256(canonicalize_json(reversed_pack).encode("utf-8")).hexdigest()

    payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "question": "Validate pack anchor mismatch.",
        "pack": {"name": pack_name, "anchor": mismatch_anchor},
        "context": {"mode": "observe", "top_k": 1},
    }

    for _ in range(2):
        resp = requests.post(f"{base_url}/api/ask", json=payload, headers=headers, timeout=20)
        assert resp.status_code in (400, 403, 409, 422, 503)
        _assert_error_contract(resp)


def test_action_fails_closed_when_postgres_down(base_url):
    if not _db_down_enabled():
        pytest.skip("CWI_EXPECT_DB_DOWN not set")

    if os.environ.get("CWI_EXPECT_RATE_LIMIT", "").lower() in ("1", "true", "yes"):
        rate_limit_raw = os.environ.get("CWI_RATE_LIMIT", "0")
        try:
            rate_limit = int(rate_limit_raw)
        except ValueError:
            rate_limit = 0
        if rate_limit > 0:
            pytest.skip("Skipping DB-down action test while rate limiting is enabled")

    headers = _auth_headers()
    if headers is None:
        pytest.skip("CWI_AUTH_TOKEN not set")

    payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "policy_mode": "approve",
        "action": {
            "type": "risk_demo",
            "payload": {"value": "test"}
        }
    }

    resp = requests.post(f"{base_url}/api/action", json=payload, headers=headers, timeout=20)
    assert resp.status_code in (502, 503)
    if resp.status_code == 503:
        if resp.headers.get("Content-Type", "").startswith("application/json") and resp.content:
            assert resp.json().get("code") == "dependency_unavailable"


def test_invalid_ask_schema_returns_400(base_url):
    if _db_down_enabled():
        pytest.skip("Skipping ask schema validation while DB is down")

    headers = _auth_headers()
    if headers is None:
        pytest.skip("CWI_AUTH_TOKEN not set")

    payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "question": 42,
        "pack": {"name": 123},
        "context": {"mode": "observe", "top_k": "bad"},
    }

    resp = requests.post(f"{base_url}/api/ask", json=payload, headers=headers, timeout=10)
    assert resp.status_code == 400
    _assert_error_contract(resp)


def test_invalid_action_schema_returns_400(base_url):
    if _db_down_enabled():
        pytest.skip("Skipping schema validation while DB is down")

    headers = _auth_headers()
    if headers is None:
        pytest.skip("CWI_AUTH_TOKEN not set")

    payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "policy_mode": "observe",
        "action": {"payload": {"value": "missing-type"}},
    }

    resp = requests.post(f"{base_url}/api/action", json=payload, headers=headers, timeout=10)
    assert resp.status_code == 400
    _assert_error_contract(resp)


def test_invalid_approval_complete_schema_returns_400(base_url):
    if _db_down_enabled():
        pytest.skip("Skipping approval schema validation while DB is down")

    headers = _auth_headers()
    if headers is None:
        pytest.skip("CWI_AUTH_TOKEN not set")

    payload = {
        "ticket_id": "ticket-1",
        "decision": "maybe",
        "approver_id": "approver-1",
    }

    resp = requests.post(f"{base_url}/api/approval/complete", json=payload, headers=headers, timeout=10)
    assert resp.status_code == 400
    _assert_error_contract(resp)


def test_rate_limit_returns_429(base_url):
    expect_rate_limit = os.environ.get("CWI_EXPECT_RATE_LIMIT", "").lower()
    if expect_rate_limit not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_RATE_LIMIT not set")

    limit_raw = os.environ.get("CWI_RATE_LIMIT", "0")
    try:
        limit = int(limit_raw)
    except ValueError:
        pytest.skip("CWI_RATE_LIMIT not valid")

    if limit <= 0:
        pytest.skip("CWI_RATE_LIMIT not enabled")

    headers = _auth_headers()
    if headers is None:
        pytest.skip("CWI_AUTH_TOKEN not set")

    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
    payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "question": "What is the system status?",
        "pack": {"name": pack_name},
        "context": {"mode": "observe", "top_k": 1},
    }

    responses = []
    for _ in range(limit + 2):
        responses.append(requests.post(f"{base_url}/api/ask", json=payload, headers=headers, timeout=10))

    rate_limited = [resp for resp in responses if resp.status_code == 429]
    assert rate_limited, "Expected at least one 429 response"

    sample = rate_limited[0].json()
    assert sample.get("code") == "rate_limited" or sample.get("error") == "rate_limited"
