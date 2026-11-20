import os
import pytest
import requests


pytestmark = [pytest.mark.integration, pytest.mark.patent]


def _verify(base_url, trace_id):
    resp = requests.get(f"{base_url}/api/ledger/verify", params={"trace_id": trace_id}, timeout=20)
    assert resp.status_code == 200
    return resp.json()


def test_ledger_verify_fails_on_invalid_signature(base_url):
    trace_id = os.environ.get("CWI_LEDGER_TRACE_ID_INVALID_SIG")
    if not trace_id:
        pytest.skip("CWI_LEDGER_TRACE_ID_INVALID_SIG not set")
    result = _verify(base_url, trace_id)
    assert result.get("isValid") is False


def test_ledger_verify_key_rotation_behavior(base_url):
    trace_old = os.environ.get("CWI_LEDGER_TRACE_ID_ROTATION_OLD")
    trace_new = os.environ.get("CWI_LEDGER_TRACE_ID_ROTATION_NEW")
    trace_revoked = os.environ.get("CWI_LEDGER_TRACE_ID_ROTATION_REVOKED")
    if not trace_old or not trace_new or not trace_revoked:
        pytest.skip("Rotation trace ids not configured")

    old_result = _verify(base_url, trace_old)
    new_result = _verify(base_url, trace_new)
    revoked_result = _verify(base_url, trace_revoked)

    assert old_result.get("isValid") is True
    assert new_result.get("isValid") is True
    assert revoked_result.get("isValid") is False


def test_ledger_export_smoke(base_url):
    trace_id = os.environ.get("CWI_LEDGER_TRACE_ID_EXPORT")
    if not trace_id:
        pytest.skip("CWI_LEDGER_TRACE_ID_EXPORT not set")

    resp = requests.get(f"{base_url}/api/ledger/export", params={"trace_id": trace_id}, timeout=20)
    assert resp.status_code == 200
    payload = resp.json()
    assert isinstance(payload.get("items"), list)


def test_ledger_verify_succeeds_on_clean_chain(base_url):
    trace_id = os.environ.get("CWI_LEDGER_TRACE_ID_CLEAN")
    if not trace_id:
        pytest.skip("CWI_LEDGER_TRACE_ID_CLEAN not set")
    result = _verify(base_url, trace_id)
    assert result.get("isValid") is True


def test_ledger_verify_fails_on_tampered_events(base_url):
    trace_id = os.environ.get("CWI_LEDGER_TRACE_ID_TAMPERED")
    if not trace_id:
        pytest.skip("CWI_LEDGER_TRACE_ID_TAMPERED not set")
    result = _verify(base_url, trace_id)
    assert result.get("isValid") is False


def test_ledger_verify_fails_on_reordered_events(base_url):
    trace_id = os.environ.get("CWI_LEDGER_TRACE_ID_REORDERED")
    if not trace_id:
        pytest.skip("CWI_LEDGER_TRACE_ID_REORDERED not set")
    result = _verify(base_url, trace_id)
    assert result.get("isValid") is False
