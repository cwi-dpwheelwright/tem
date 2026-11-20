import json
import os
from pathlib import Path

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
    path = Path(value)
    if path.exists():
        return json.loads(path.read_text(encoding="utf-8"))
    return json.loads(value)


def test_patent7_multiband_telemetry_ingest(base_url):
    payload = _load_payload("CWI_PATENT7_TELEMETRY_PAYLOAD_JSON")
    if payload is None:
        pytest.skip("CWI_PATENT7_TELEMETRY_PAYLOAD_JSON not set")
    url = os.environ.get("CWI_PATENT7_TELEMETRY_URL", f"{base_url}/api/ingest/telemetry")
    resp = requests.post(url, json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code == 200
    data = resp.json()
    assert data.get("accepted") is True
    trace = data.get("trace") or {}
    assert trace.get("trace_id")


def test_patent10_ownership_governance_action_requires_approval(base_url):
    payload = _load_payload("CWI_PATENT10_ACTION_PAYLOAD_JSON")
    if payload is None:
        pytest.skip("CWI_PATENT10_ACTION_PAYLOAD_JSON not set")
    resp = requests.post(f"{base_url}/api/action", json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code == 200
    data = resp.json()
    assert data.get("approval_ticket") or data.get("executed") is False


def test_patent11_report_generation_action_ticketed(base_url):
    payload = _load_payload("CWI_PATENT11_ACTION_PAYLOAD_JSON")
    if payload is None:
        pytest.skip("CWI_PATENT11_ACTION_PAYLOAD_JSON not set")
    resp = requests.post(f"{base_url}/api/action", json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code == 200
    data = resp.json()
    assert data.get("approval_ticket") or data.get("executed") is False


def test_patent10_ownership_transfer_and_revoke(base_url):
    payload = _load_payload("CWI_PATENT10_OWNERSHIP_TRANSFER_JSON")
    if payload is None:
        pytest.skip("CWI_PATENT10_OWNERSHIP_TRANSFER_JSON not set")
    url = os.environ.get("CWI_PATENT10_OWNERSHIP_TRANSFER_URL", f"{base_url}/api/ownership/transfer")
    resp = requests.post(url, json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code == 200
    data = resp.json()
    request_id = data.get("request_id")
    assert request_id

    revoke_payload = {
        "request_id": request_id,
        "actor_id": payload.get("from_user_id"),
        "reason": "gold-test-revoke",
    }
    revoke_url = os.environ.get("CWI_PATENT10_OWNERSHIP_REVOKE_URL", f"{base_url}/api/ownership/revoke")
    revoke = requests.post(revoke_url, json=revoke_payload, headers=_auth_headers(), timeout=20)
    assert revoke.status_code == 200
    revoke_data = revoke.json()
    assert revoke_data.get("status") == "revoked"


def test_patent10_ownership_delegate(base_url):
    payload = _load_payload("CWI_PATENT10_OWNERSHIP_DELEGATE_JSON")
    if payload is None:
        pytest.skip("CWI_PATENT10_OWNERSHIP_DELEGATE_JSON not set")
    url = os.environ.get("CWI_PATENT10_OWNERSHIP_DELEGATE_URL", f"{base_url}/api/ownership/delegate")
    resp = requests.post(url, json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code == 200
    data = resp.json()
    assert data.get("request_id")


def test_patent12_placement_optimization_ask(base_url):
    payload = _load_payload("CWI_PATENT12_ASK_PAYLOAD_JSON")
    if payload is None:
        pytest.skip("CWI_PATENT12_ASK_PAYLOAD_JSON not set")
    resp = requests.post(f"{base_url}/api/ask", json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code == 200
    data = resp.json()
    assert data.get("answer")
    assert (data.get("trace") or {}).get("trace_id")


def test_patent13_dual_format_outputs(base_url):
    payload = _load_payload("CWI_PATENT13_ASK_PAYLOAD_JSON")
    if payload is None:
        pytest.skip("CWI_PATENT13_ASK_PAYLOAD_JSON not set")
    resp = requests.post(f"{base_url}/api/ask", json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code == 200
    data = resp.json()
    assert data.get("answer")

    expected_fields = [
        field.strip()
        for field in os.environ.get("CWI_PATENT13_EXPECT_FIELDS", "").split(",")
        if field.strip()
    ]
    for field in expected_fields:
        assert field in data, f"Expected field '{field}' in response"


def test_patent3_survival_signal_and_offline_response(base_url):
    payload = _load_payload("CWI_PATENT3_SURVIVAL_SIGNAL_JSON")
    if payload is None:
        pytest.skip("CWI_PATENT3_SURVIVAL_SIGNAL_JSON not set")

    resp = requests.post(f"{base_url}/api/survival/signal", json=payload, timeout=20)
    assert resp.status_code == 200
    data = resp.json()
    assert data.get("mode") in ("normal", "degraded", "survival")

    offline = requests.get(f"{base_url}/api/survival/offline-response", timeout=20)
    assert offline.status_code == 200
    offline_data = offline.json()
    assert "message" in offline_data


def test_patent3_survival_degrade_survive_recover(base_url):
    if os.environ.get("CWI_EXPECT_SURVIVAL_RECOVERY", "").lower() not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_SURVIVAL_RECOVERY not set")

    headers = _auth_headers()

    degraded_signal = {
        "signal_type": "gold-test",
        "severity": 0.75,
        "details": "degraded",
    }
    degraded = requests.post(
        f"{base_url}/api/survival/signal",
        json=degraded_signal,
        headers=headers,
        timeout=20,
    )
    assert degraded.status_code == 200
    degraded_payload = degraded.json()
    assert degraded_payload.get("profile") == "degraded"
    assert degraded_payload.get("mode") == "degraded"

    critical_signal = {
        "signal_type": "gold-test",
        "severity": 0.95,
        "details": "critical",
    }
    critical = requests.post(
        f"{base_url}/api/survival/signal",
        json=critical_signal,
        headers=headers,
        timeout=20,
    )
    assert critical.status_code == 200
    critical_payload = critical.json()
    assert critical_payload.get("profile") == "critical"
    assert critical_payload.get("mode") == "survival"

    recover_signal = {
        "signal_type": "gold-test",
        "severity": 0.2,
        "details": "recovery",
    }
    recovered = requests.post(
        f"{base_url}/api/survival/signal",
        json=recover_signal,
        headers=headers,
        timeout=20,
    )
    assert recovered.status_code == 200
    recovered_payload = recovered.json()
    assert recovered_payload.get("mode") == "normal"


def test_patent9_reconciliation_upsert(base_url):
    payload = _load_payload("CWI_PATENT9_RECONCILE_PAYLOAD_JSON")
    if payload is None:
        pytest.skip("CWI_PATENT9_RECONCILE_PAYLOAD_JSON not set")

    resp = requests.post(f"{base_url}/api/reconciliation/records", json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code == 200
    data = resp.json()
    assert data.get("record_id")
    assert "updated_utc" in data


def test_provenance_ingest(base_url):
    payload = _load_payload("CWI_PROVENANCE_INGEST_PAYLOAD_JSON")
    if payload is None:
        pytest.skip("CWI_PROVENANCE_INGEST_PAYLOAD_JSON not set")

    url = os.environ.get("CWI_PROVENANCE_INGEST_URL", f"{base_url}/api/ingest/provenance")
    resp = requests.post(url, json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code in (200, 202, 204)
