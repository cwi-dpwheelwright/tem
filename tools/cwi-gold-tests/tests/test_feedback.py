import json
import os
import time

import pytest
import requests


pytestmark = pytest.mark.integration


def _auth_headers():
    token = os.environ.get("CWI_AUTH_TOKEN")
    if not token:
        return {}
    return {"Authorization": f"Bearer {token}"}


def _required(name):
    value = os.environ.get(name)
    if not value:
        pytest.skip(f"{name} not set")
    return value


def _feedback_payload(consent_granted=True, idempotency_key=None, note=None):
    tenant_id = _required("CWI_FEEDBACK_TENANT_ID")
    trace_id = _required("CWI_FEEDBACK_TRACE_ID")
    actor_id = _required("CWI_FEEDBACK_ACTOR_ID")
    feedback = os.environ.get("CWI_FEEDBACK_TYPE", "up")

    payload = {
        "tenant_id": tenant_id,
        "trace_id": trace_id,
        "actor_id": actor_id,
        "feedback": feedback,
        "consent_granted": consent_granted,
    }

    if idempotency_key:
        payload["idempotency_key"] = idempotency_key

    if note is not None:
        payload["note"] = note

    return payload


def _ledger_items(base_url, trace_id):
    resp = requests.get(f"{base_url}/api/ledger/export", params={"trace_id": trace_id}, timeout=20)
    assert resp.status_code == 200
    payload = resp.json()
    return payload.get("items", [])


def _find_feedback_event(items):
    for item in items:
        if item.get("event_type") == "OUTCOME_FEEDBACK_RECORDED":
            return item
    return None


def test_feedback_minimal_payload_appends_ledger_event(base_url):
    payload = _feedback_payload()
    resp = requests.post(f"{base_url}/api/feedback", json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code == 200
    data = resp.json()
    assert data.get("trace_id") == payload["trace_id"]
    assert data.get("tenant_id") == payload["tenant_id"]
    assert data.get("summary")

    if os.environ.get("CWI_FEEDBACK_VERIFY_LEDGER", "").lower() not in ("1", "true", "yes"):
        pytest.skip("CWI_FEEDBACK_VERIFY_LEDGER not set; skipping ledger event assertion.")

    items = _ledger_items(base_url, payload["trace_id"])
    assert _find_feedback_event(items) is not None


def test_feedback_idempotent_duplicates(base_url):
    idempotency_key = os.environ.get("CWI_FEEDBACK_IDEMPOTENCY_KEY")
    if not idempotency_key:
        pytest.skip("CWI_FEEDBACK_IDEMPOTENCY_KEY not set")

    payload = _feedback_payload(idempotency_key=idempotency_key)
    first = requests.post(f"{base_url}/api/feedback", json=payload, headers=_auth_headers(), timeout=20)
    assert first.status_code == 200
    first_data = first.json()

    second = requests.post(f"{base_url}/api/feedback", json=payload, headers=_auth_headers(), timeout=20)
    assert second.status_code == 200
    second_data = second.json()

    assert first_data.get("idempotency_key") == second_data.get("idempotency_key")
    assert first_data.get("summary") == second_data.get("summary")

    summary = requests.get(
        f"{base_url}/api/feedback/summary",
        params={"tenant_id": payload["tenant_id"], "trace_id": payload["trace_id"]},
        headers=_auth_headers(),
        timeout=20,
    )
    assert summary.status_code == 200
    summary_data = summary.json()
    assert summary_data.get("tenant_id") == payload["tenant_id"]
    assert summary_data.get("trace_id") == payload["trace_id"]


def test_feedback_rejects_missing_consent(base_url):
    payload = _feedback_payload(consent_granted=False)
    resp = requests.post(f"{base_url}/api/feedback", json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code == 403
    body = resp.json()
    assert body.get("error") == "forbidden" or "consent" in str(body.get("message", "")).lower()


def test_feedback_redacts_notes_in_ledger(base_url):
    if os.environ.get("CWI_FEEDBACK_REDACTION_ENABLED", "").lower() not in ("1", "true", "yes"):
        pytest.skip("CWI_FEEDBACK_REDACTION_ENABLED not set")

    note = os.environ.get("CWI_FEEDBACK_NOTE", "secret-feedback-note")
    payload = _feedback_payload(note=note)
    resp = requests.post(f"{base_url}/api/feedback", json=payload, headers=_auth_headers(), timeout=20)
    assert resp.status_code == 200

    items = _ledger_items(base_url, payload["trace_id"])
    event = _find_feedback_event(items)
    assert event is not None

    payload_json = event.get("payload_json") or ""
    parsed = json.loads(payload_json)
    assert parsed.get("notes") == "[REDACTED]"
