import os

import pytest
import requests


pytestmark = [pytest.mark.integration, pytest.mark.patent]


def _auth_headers():
    headers = {}
    token = os.environ.get("CWI_AUTH_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    break_glass = os.environ.get("CWI_BREAK_GLASS_TOKEN")
    if break_glass:
        headers["X-Break-Glass-Token"] = break_glass
    return headers


def _required(name):
    value = os.environ.get(name)
    if not value:
        pytest.skip(f"{name} not set")
    return value


def _ledger_items(base_url, trace_id):
    resp = requests.get(f"{base_url}/api/ledger/export", params={"trace_id": trace_id}, timeout=20)
    assert resp.status_code == 200
    return resp.json().get("items", [])


def test_pack_activation_and_rollback_audited(base_url):
    tenant_id = _required("CWI_PACK_ACTIVATION_TENANT_ID")
    pack_name = _required("CWI_PACK_ACTIVATION_PACK_NAME")
    pack_hash = _required("CWI_PACK_ACTIVATION_PACK_HASH")
    actor_id = _required("CWI_PACK_ACTIVATION_ACTOR_ID")
    rollback_reason = os.environ.get("CWI_PACK_ACTIVATION_ROLLBACK_REASON", "gold-test-rollback")

    activation = {
        "tenant_id": tenant_id,
        "pack_name": pack_name,
        "pack_hash": pack_hash,
        "actor_user_id": actor_id,
    }

    resp = requests.post(
        f"{base_url}/api/packs/activation",
        json=activation,
        headers=_auth_headers(),
        timeout=20,
    )
    assert resp.status_code == 200
    data = resp.json()
    assert data.get("status") == "active"
    assert data.get("pack_hash") == pack_hash

    rollback = {
        "tenant_id": tenant_id,
        "pack_name": pack_name,
        "actor_user_id": actor_id,
        "reason": rollback_reason,
    }
    rolled = requests.post(
        f"{base_url}/api/packs/rollback",
        json=rollback,
        headers=_auth_headers(),
        timeout=20,
    )
    assert rolled.status_code == 200
    rollback_data = rolled.json()
    assert rollback_data.get("status") == "rolled_back"

    trace_id = f"pack-activation:{tenant_id}:{pack_name}"
    items = _ledger_items(base_url, trace_id)
    event_types = [item.get("event_type") for item in items]
    assert "pack.activation" in event_types
    assert "pack.rollback" in event_types


def test_invalid_pack_signature_or_dependency_fails_closed(base_url):
    expect_invalid = os.environ.get("CWI_EXPECT_PACK_INVALID", "").lower()
    if expect_invalid not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_PACK_INVALID not set")

    pack_name = os.environ.get("CWI_INVALID_PACK_NAME")
    if not pack_name:
        pytest.skip("CWI_INVALID_PACK_NAME not set")

    payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "question": "Validate pack integrity.",
        "pack": {"name": pack_name},
        "context": {"mode": "observe", "top_k": 1},
    }

    resp = requests.post(f"{base_url}/api/ask", json=payload, headers=_auth_headers(), timeout=20)
    expected = os.environ.get("CWI_INVALID_PACK_EXPECT_STATUS", "400,403,422,503")
    expected_codes = {int(code.strip()) for code in expected.split(",") if code.strip().isdigit()}
    assert resp.status_code in expected_codes
    body = resp.text.lower()
    if resp.headers.get("Content-Type", "").startswith("application/json") and resp.content:
        body = str(resp.json()).lower()
    assert "pack" in body
