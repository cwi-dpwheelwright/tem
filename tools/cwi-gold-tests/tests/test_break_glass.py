import os
import time
from datetime import datetime, timezone

import pytest
import requests


pytestmark = pytest.mark.integration


def _auth_headers():
    token = os.environ.get("CWI_AUTH_TOKEN")
    if not token:
        return {}
    return {"Authorization": f"Bearer {token}"}


def _resolve_role_scope():
    role = os.environ.get("CWI_BREAK_GLASS_ROLE")
    scope = os.environ.get("CWI_BREAK_GLASS_SCOPE")
    if role and scope:
        return role, scope

    roles = [r.strip() for r in os.environ.get("BREAK_GLASS_ROLES", "").split(",") if r.strip()]
    scopes = [s.strip() for s in os.environ.get("BREAK_GLASS_SCOPES", "").split(",") if s.strip()]
    if roles and scopes:
        return roles[0], scopes[0]

    return None, None


def _ledger_items_or_fail(base_url, trace_id, context):
    export = requests.get(
        f"{base_url}/api/ledger/export",
        params={"trace_id": trace_id},
        headers=_auth_headers(),
        timeout=20,
    )
    if export.status_code != 200:
        snippet = export.text[:500]
        pytest.fail(
            f"{context}: ledger export failed (status={export.status_code}). "
            f"Response snippet: {snippet!r}. Hint: verify ledger service is reachable."
        )
    try:
        payload = export.json()
    except ValueError:
        snippet = export.text[:500]
        pytest.fail(
            f"{context}: ledger export returned non-JSON response. "
            f"Response snippet: {snippet!r}."
        )
    return payload.get("items", [])


def _issue_token(base_url, duration_minutes, reason):
    role, scope = _resolve_role_scope()
    if not role or not scope:
        pytest.skip("Break-glass roles/scopes not configured for tests.")

    payload = {
        "tenant_id": "tenant-1",
        "user_id": "user-1",
        "role": role,
        "scope": scope,
        "duration_minutes": duration_minutes,
        "reason": reason,
    }

    headers = _auth_headers()
    resp = requests.post(f"{base_url}/api/break-glass/issue", json=payload, headers=headers, timeout=20)
    return resp


def _parse_utc(value):
    if not value:
        return None
    text = str(value).replace("Z", "+00:00")
    dt = datetime.fromisoformat(text)
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return dt.astimezone(timezone.utc)


def test_break_glass_issue_requires_reason_and_enforces_duration(base_url):
    require_reason = os.environ.get("BREAK_GLASS_REQUIRE_REASON", "true").lower() in ("1", "true", "yes")
    max_minutes = int(os.environ.get("BREAK_GLASS_MAX_MINUTES", "60"))

    if require_reason:
        missing_reason = _issue_token(base_url, duration_minutes=1, reason=None)
        assert missing_reason.status_code == 400

    over_limit = _issue_token(base_url, duration_minutes=max_minutes + 1, reason="test")
    assert over_limit.status_code == 400


def test_break_glass_revocation_invalidates_and_logs_ledger_event(base_url):
    issue = _issue_token(base_url, duration_minutes=1, reason="integration-test")
    assert issue.status_code == 200
    token_id = issue.json().get("token_id")
    assert token_id

    headers = _auth_headers()
    revoke = requests.post(
        f"{base_url}/api/break-glass/revoke",
        json={"token_id": token_id},
        headers=headers,
        timeout=20,
    )
    assert revoke.status_code == 200

    items = _ledger_items_or_fail(base_url, token_id, "break_glass_revocation")
    event_types = [item.get("event_type") for item in items]
    assert "break_glass.revoked" in event_types


def test_break_glass_expired_token_denies_protected_endpoint(base_url):
    protected_path = os.environ.get("CWI_BREAK_GLASS_PROTECTED_PATH")
    wait_seconds = int(os.environ.get("CWI_BREAK_GLASS_EXPIRY_WAIT_SECONDS", "0"))
    if not protected_path or wait_seconds <= 0:
        pytest.skip("Break-glass expiry test not configured.")

    issue = _issue_token(base_url, duration_minutes=1, reason="expiry-test")
    assert issue.status_code == 200
    token_id = issue.json().get("token_id")
    assert token_id

    time.sleep(wait_seconds)

    headers = _auth_headers()
    headers["X-Break-Glass-Token"] = token_id
    resp = requests.post(f"{base_url}{protected_path}", json={}, headers=headers, timeout=20)
    assert resp.status_code == 403


def test_break_glass_lifecycle_timeboxed_and_logged(base_url):
    duration_minutes = int(os.environ.get("BREAK_GLASS_TEST_DURATION_MINUTES", "1"))
    issue = _issue_token(base_url, duration_minutes=duration_minutes, reason="lifecycle-test")
    if issue.status_code in (401, 403):
        pytest.skip("Break-glass issue requires auth token.")
    assert issue.status_code == 200
    payload = issue.json()
    token_id = payload.get("token_id")
    assert token_id

    audit = requests.get(
        f"{base_url}/api/break-glass/audit",
        params={"tenant_id": "tenant-1", "page": 1, "page_size": 50},
        headers=_auth_headers(),
        timeout=20,
    )
    if audit.status_code in (401, 403):
        pytest.skip("Break-glass audit requires auth token.")
    assert audit.status_code == 200
    items = audit.json().get("items", [])
    item = next((entry for entry in items if entry.get("token_id") == token_id), None)
    assert item is not None

    created = _parse_utc(item.get("created_utc"))
    expires = _parse_utc(item.get("expires_utc"))
    assert created and expires
    delta = abs((expires - created).total_seconds() - duration_minutes * 60)
    assert delta <= 5

    response_expires = _parse_utc(payload.get("expires_utc"))
    assert response_expires
    assert abs((response_expires - expires).total_seconds()) <= 1

    headers = _auth_headers()
    headers["X-Break-Glass-Token"] = token_id
    allow = requests.post(f"{base_url}/api/break-glass/protected", json={}, headers=headers, timeout=20)
    assert allow.status_code == 200

    revoke = requests.post(
        f"{base_url}/api/break-glass/revoke",
        json={"token_id": token_id},
        headers=_auth_headers(),
        timeout=20,
    )
    assert revoke.status_code == 200

    deny = requests.post(f"{base_url}/api/break-glass/protected", json={}, headers=headers, timeout=20)
    assert deny.status_code == 403

    items = _ledger_items_or_fail(base_url, token_id, "break_glass_lifecycle")
    event_types = [item.get("event_type") for item in items]
    assert "break_glass.issued" in event_types
    assert "break_glass.revoked" in event_types
