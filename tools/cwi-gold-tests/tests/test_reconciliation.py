import os
import time
import uuid

import pytest
import requests


pytestmark = [pytest.mark.integration, pytest.mark.patent]


def _auth_headers():
    token = os.environ.get("CWI_AUTH_TOKEN")
    if not token:
        return {}
    return {"Authorization": f"Bearer {token}"}


def _now_iso():
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


def _payload(record_id: str):
    updated = _now_iso()
    return {
        "record_id": record_id,
        "tenant_id": os.environ.get("CWI_RECON_TENANT_ID", "tenant-1"),
        "user_id": os.environ.get("CWI_RECON_USER_ID", "user-1"),
        "sources": [
            {
                "source_id": "source-a",
                "updated_utc": updated,
                "attributes": {"status": "active", "region": "us"},
            },
            {
                "source_id": "source-b",
                "updated_utc": updated,
                "attributes": {"status": "inactive", "region": "us"},
            },
        ],
    }


def _record_id():
    value = os.environ.get("CWI_RECON_RECORD_ID")
    if value:
        return value
    return f"gold-recon-{uuid.uuid4().hex}"


def _recon_base_url(default_base: str) -> str:
    override = os.environ.get("CWI_RECON_BASE_URL")
    if override:
        return override.rstrip("/")
    port = os.environ.get("ORCHESTRATOR_HOST_PORT")
    if port:
        return f"http://localhost:{port}"
    for name in ("CWI_ORCHESTRATOR_BASE_URL", "ORCHESTRATOR_BASE_URL", "ORCHESTRATOR_URL"):
        value = os.environ.get(name)
        if value:
            return value.rstrip("/")
    return default_base.rstrip("/")


def test_reconciliation_conflict_detected(base_url):
    base_url = _recon_base_url(base_url)
    record_id = _record_id()
    resp = requests.post(
        f"{base_url}/api/reconciliation/records",
        json=_payload(record_id),
        headers=_auth_headers(),
        timeout=20,
    )
    assert resp.status_code == 200
    data = resp.json()
    conflicts = data.get("conflicts") or []
    assert isinstance(conflicts, list)

    conflict = next((item for item in conflicts if item.get("field") == "status"), None)
    assert conflict is not None
    values = set(value.lower() for value in conflict.get("values", []))
    assert {"active", "inactive"}.issubset(values)


def test_reconciliation_conflict_requires_approval_ticket(base_url):
    base_url = _recon_base_url(base_url)
    record_id = _record_id()
    resp = requests.post(
        f"{base_url}/api/reconciliation/records",
        json=_payload(record_id),
        headers=_auth_headers(),
        timeout=20,
    )
    assert resp.status_code == 200
    data = resp.json()
    approval = data.get("approval_ticket") or {}
    assert approval.get("ticket_id")
    assert approval.get("risk") in ("high", "critical", "medium") or approval.get("risk")


def test_reconciliation_get_record(base_url):
    base_url = _recon_base_url(base_url)
    record_id = _record_id()
    resp = requests.post(
        f"{base_url}/api/reconciliation/records",
        json=_payload(record_id),
        headers=_auth_headers(),
        timeout=20,
    )
    assert resp.status_code == 200

    get_resp = requests.get(
        f"{base_url}/api/reconciliation/records/{record_id}",
        headers=_auth_headers(),
        timeout=20,
    )
    assert get_resp.status_code == 200
    data = get_resp.json()
    assert data.get("record_id") == record_id
    conflicts = data.get("conflicts") or []
    assert isinstance(conflicts, list)


def test_reconciliation_report_endpoint(base_url):
    base_url = _recon_base_url(base_url)
    record_id = _record_id()
    resp = requests.post(
        f"{base_url}/api/reconciliation/records",
        json=_payload(record_id),
        headers=_auth_headers(),
        timeout=20,
    )
    assert resp.status_code == 200

    report_resp = requests.get(
        f"{base_url}/api/reconciliation/records/{record_id}/report",
        headers=_auth_headers(),
        timeout=20,
    )
    assert report_resp.status_code == 200
    report = report_resp.json()
    assert report.get("record_id") == record_id
    assert "updated_utc" in report


def test_reconciliation_approval_resolution_updates_record(base_url):
    base_url = _recon_base_url(base_url)
    record_id = _record_id()
    resp = requests.post(
        f"{base_url}/api/reconciliation/records",
        json=_payload(record_id),
        headers=_auth_headers(),
        timeout=20,
    )
    assert resp.status_code == 200
    data = resp.json()
    approval = data.get("approval_ticket") or {}
    ticket_id = approval.get("ticket_id")
    assert ticket_id

    payload = {
        "ticket_id": ticket_id,
        "decision": "approve",
        "approver_id": os.environ.get("CWI_RECON_APPROVER_ID", "approver-1"),
        "reason": "reconciliation approved",
    }
    complete = requests.post(
        f"{base_url}/api/reconciliation/records/{record_id}/approval/complete",
        json=payload,
        headers=_auth_headers(),
        timeout=20,
    )
    assert complete.status_code == 200
    complete_data = complete.json()
    assert complete_data.get("record_id") == record_id
    assert complete_data.get("ticket_id") == ticket_id
    assert complete_data.get("status") == "approved"
    assert complete_data.get("conflicts_count") == 0

    get_resp = requests.get(
        f"{base_url}/api/reconciliation/records/{record_id}",
        headers=_auth_headers(),
        timeout=20,
    )
    assert get_resp.status_code == 200
    record = get_resp.json()
    conflicts = record.get("conflicts") or []
    assert conflicts == []

    if os.environ.get("CWI_RECON_VERIFY_LEDGER", "").lower() not in ("1", "true", "yes"):
        pytest.skip("CWI_RECON_VERIFY_LEDGER not set; skipping ledger event assertion.")

    ledger = requests.get(
        f"{base_url}/api/ledger/export",
        params={"trace_id": record_id},
        headers=_auth_headers(),
        timeout=20,
    )
    assert ledger.status_code == 200
    items = ledger.json().get("items", [])
    assert any(item.get("event_type") == "reconciliation.approval_decision" for item in items)
