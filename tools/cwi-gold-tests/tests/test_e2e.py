import os
import requests
import pytest


def _auth_headers():
    token = os.environ.get("CWI_AUTH_TOKEN")
    if not token:
        return None
    return {"Authorization": f"Bearer {token}"}


def _tenant_id():
    return os.environ.get("CWI_TENANT_ID", "tenant-1")


@pytest.mark.e2e
def test_action_returns_approval_ticket(base_url):
    headers = _auth_headers()
    if headers is None:
        pytest.skip("CWI_AUTH_TOKEN not set")

    payload = {
        "tenant_id": _tenant_id(),
        "user_id": "user-1",
        "policy_mode": "approve",
        "action": {
            "type": "risk_demo",
            "payload": {"value": "test"}
        }
    }

    resp = requests.post(f"{base_url}/api/action", json=payload, headers=headers, timeout=20)
    assert resp.status_code == 200

    data = resp.json()
    assert data.get("executed") is False
    assert data.get("approval_ticket") is not None


@pytest.mark.e2e
def test_approval_complete_idempotent(base_url):
    headers = _auth_headers()
    if headers is None:
        pytest.skip("CWI_AUTH_TOKEN not set")

    action_payload = {
        "tenant_id": _tenant_id(),
        "user_id": "user-1",
        "policy_mode": "approve",
        "action": {
            "type": "risk_demo",
            "payload": {"value": "test"}
        }
    }

    action_resp = requests.post(f"{base_url}/api/action", json=action_payload, headers=headers, timeout=20)
    assert action_resp.status_code == 200
    ticket = action_resp.json().get("approval_ticket")
    assert ticket is not None

    complete_payload = {
        "ticket_id": ticket["ticket_id"],
        "decision": "approve",
        "approver_id": "approver-1",
        "tenant_id": _tenant_id()
    }

    first = requests.post(f"{base_url}/api/approval/complete", json=complete_payload, headers=headers, timeout=20)
    second = requests.post(f"{base_url}/api/approval/complete", json=complete_payload, headers=headers, timeout=20)

    assert first.status_code == 200
    assert second.status_code == 200


@pytest.mark.e2e
def test_action_blocked_when_kill_switch_enabled(base_url):
    expect_kill_switch = os.environ.get("CWI_EXPECT_KILL_SWITCH", "").lower()
    if expect_kill_switch not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_KILL_SWITCH not set")

    headers = _auth_headers()
    if headers is None:
        pytest.skip("CWI_AUTH_TOKEN not set")

    tenant_id = os.environ.get("CWI_TENANT_ID", "tenant-1")
    payload = {
        "tenant_id": tenant_id,
        "user_id": "user-1",
        "policy_mode": "approve",
        "action": {
            "type": "risk_demo",
            "payload": {"value": "test"}
        }
    }

    resp = requests.post(f"{base_url}/api/action", json=payload, headers=headers, timeout=20)
    assert resp.status_code == 403
    assert resp.json().get("code") == "forbidden"


@pytest.mark.e2e
def test_action_fails_closed_when_postgres_down(base_url):
    expect_db_down = os.environ.get("CWI_EXPECT_DB_DOWN", "").lower()
    if expect_db_down not in ("1", "true", "yes"):
        pytest.skip("CWI_EXPECT_DB_DOWN not set")

    headers = _auth_headers()
    if headers is None:
        pytest.skip("CWI_AUTH_TOKEN not set")

    payload = {
        "tenant_id": _tenant_id(),
        "user_id": "user-1",
        "policy_mode": "approve",
        "action": {
            "type": "risk_demo",
            "payload": {"value": "test"}
        }
    }

    resp = requests.post(f"{base_url}/api/action", json=payload, headers=headers, timeout=20)
    assert resp.status_code == 503
    assert resp.json().get("code") == "dependency_unavailable"
