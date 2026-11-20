import datetime
import hashlib
import json
import os
import pytest
import requests


pytestmark = [pytest.mark.integration, pytest.mark.parity]


def _truthy(value):
    return str(value or "").lower() in ("1", "true", "yes")


def _router_url():
    value = os.environ.get("CWI_ROUTER_BASE_URL") or os.environ.get("ROUTER_BASE_URL")
    if not value:
        pytest.skip("CWI_ROUTER_BASE_URL not set")
    return value.rstrip("/")


def _packs_base(base_url):
    return os.environ.get("CWI_PACKS_BASE_URL", base_url).rstrip("/")


def _auth_headers():
    token = os.environ.get("CWI_AUTH_TOKEN")
    if not token:
        return None
    return {"Authorization": f"Bearer {token}"}


def _ask_payload(pack_name, anchor=None):
    payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "question": "Parity check",
        "pack": {"name": pack_name},
        "context": {"mode": "observe", "top_k": 1},
    }
    if anchor:
        payload["pack"]["anchor"] = anchor
    return payload


def _reverse_keys(value):
    if isinstance(value, dict):
        return {key: _reverse_keys(value[key]) for key in sorted(value.keys(), reverse=True)}
    if isinstance(value, list):
        return [_reverse_keys(item) for item in value]
    return value


def _canonicalize_json(data):
    return json.dumps(data, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


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
            {"tag": "cpu_util", "unit": "%", "series": [0.4, 0.6, 0.5]},
            {"tag": "mem_util", "unit": "%", "series": [0.2, 0.3, 0.25]},
        ],
        "metadata": {"source": "gold-tests-parity"},
    }


def _assert_error_sanitized(payload):
    if not isinstance(payload, dict):
        return
    serialized = json.dumps(payload).lower()
    assert "stack" not in serialized
    assert "exception" not in serialized
    assert "traceback" not in serialized


def test_parity_auth_enforcement(base_url):
    if not _truthy(os.environ.get("CWI_PARITY_AUTH")):
        pytest.skip("CWI_PARITY_AUTH not set")

    auth_required = os.environ.get("AUTH_REQUIRED")
    if auth_required is not None and not _truthy(auth_required):
        pytest.skip("AUTH_REQUIRED is false; auth parity not applicable")

    router_url = _router_url()
    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
    payload = _ask_payload(pack_name)

    gateway_resp = requests.post(f"{base_url}/api/ask", json=payload, timeout=20)
    router_resp = requests.post(f"{router_url}/api/ask", json=payload, timeout=20)

    assert gateway_resp.status_code in (401, 403)
    assert router_resp.status_code in (401, 403)
    assert gateway_resp.status_code == router_resp.status_code

    if gateway_resp.headers.get("Content-Type", "").startswith("application/json"):
        _assert_error_sanitized(gateway_resp.json())
    if router_resp.headers.get("Content-Type", "").startswith("application/json"):
        _assert_error_sanitized(router_resp.json())


def test_parity_pack_anchor_mismatch(base_url):
    if not _truthy(os.environ.get("CWI_PARITY_PACK_TRUST")):
        pytest.skip("CWI_PARITY_PACK_TRUST not set")

    headers = _auth_headers()
    if headers is None:
        pytest.skip("CWI_AUTH_TOKEN not set")

    router_url = _router_url()
    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")

    packs_base = _packs_base(base_url)
    pack_resp = requests.get(f"{packs_base}/packs/{pack_name}/pack.json", timeout=10)
    anchor_resp = requests.get(f"{packs_base}/packs/{pack_name}/anchor.txt", timeout=10)
    assert pack_resp.status_code == 200
    assert anchor_resp.status_code == 200

    pack_json = pack_resp.json()
    reversed_pack = _reverse_keys(pack_json)
    try:
        canonical = _canonicalize_json(reversed_pack)
    except TypeError:
        canonical = json.dumps(reversed_pack)
    mismatch_digest = hashlib.sha256(canonical.encode("utf-8")).hexdigest()
    anchor = anchor_resp.text.strip()
    if mismatch_digest == anchor:
        reversed_pack["tampered"] = True
        canonical = _canonicalize_json(reversed_pack)
        mismatch_digest = hashlib.sha256(canonical.encode("utf-8")).hexdigest()

    payload = _ask_payload(pack_name, anchor=mismatch_digest)

    gateway_resp = requests.post(f"{base_url}/api/ask", json=payload, headers=headers, timeout=20)
    router_resp = requests.post(f"{router_url}/api/ask", json=payload, headers=headers, timeout=20)

    assert gateway_resp.status_code == router_resp.status_code
    assert gateway_resp.status_code in (400, 403, 409, 422, 503)

    if gateway_resp.headers.get("Content-Type", "").startswith("application/json"):
        _assert_error_sanitized(gateway_resp.json())
    if router_resp.headers.get("Content-Type", "").startswith("application/json"):
        _assert_error_sanitized(router_resp.json())


def test_parity_rate_limit(base_url):
    if not _truthy(os.environ.get("CWI_PARITY_RATE_LIMIT")):
        pytest.skip("CWI_PARITY_RATE_LIMIT not set")

    headers = _auth_headers()
    if headers is None:
        pytest.skip("CWI_AUTH_TOKEN not set")

    router_url = _router_url()
    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
    payload = _ask_payload(pack_name)

    limit_raw = os.environ.get("CWI_RATE_LIMIT", "0")
    try:
        limit = int(limit_raw)
    except ValueError:
        pytest.skip("CWI_RATE_LIMIT not valid")
    if limit <= 0:
        pytest.skip("CWI_RATE_LIMIT not enabled")

    gateway_codes = []
    router_codes = []
    for _ in range(limit + 2):
        gateway_codes.append(requests.post(f"{base_url}/api/ask", json=payload, headers=headers, timeout=20).status_code)
        router_codes.append(requests.post(f"{router_url}/api/ask", json=payload, headers=headers, timeout=20).status_code)

    assert any(code == 429 for code in gateway_codes)
    assert any(code == 429 for code in router_codes)


def test_parity_telemetry_ingest(base_url):
    if not _truthy(os.environ.get("CWI_PARITY_TELEMETRY")):
        pytest.skip("CWI_PARITY_TELEMETRY not set")

    headers = _auth_headers()
    if headers is None:
        pytest.skip("CWI_AUTH_TOKEN not set")

    router_url = _router_url()
    payload = _telemetry_payload()

    gateway_resp = requests.post(f"{base_url}/api/ingest/telemetry", json=payload, headers=headers, timeout=20)
    router_resp = requests.post(f"{router_url}/api/ingest/telemetry", json=payload, headers=headers, timeout=20)

    assert gateway_resp.status_code == router_resp.status_code
    assert gateway_resp.status_code in (200, 400, 401, 403)

    if gateway_resp.headers.get("Content-Type", "").startswith("application/json"):
        _assert_error_sanitized(gateway_resp.json())
    if router_resp.headers.get("Content-Type", "").startswith("application/json"):
        _assert_error_sanitized(router_resp.json())
