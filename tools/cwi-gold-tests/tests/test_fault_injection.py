import os

import pytest
import requests


pytestmark = pytest.mark.integration


def _truthy(value):
    return str(value or "").lower() in ("1", "true", "yes")


def _headers():
    headers = {}
    token = os.environ.get("CWI_AUTH_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    break_glass = os.environ.get("CWI_BREAK_GLASS_TOKEN")
    if break_glass:
        headers["X-Break-Glass-Token"] = break_glass
    return headers


def test_fault_injection_exception_maps_to_error_contract(base_url):
    if not _truthy(os.environ.get("CWI_FAULT_INJECTION_ENABLED")):
        pytest.skip("CWI_FAULT_INJECTION_ENABLED not set")

    if not os.environ.get("CWI_BREAK_GLASS_TOKEN"):
        pytest.skip("CWI_BREAK_GLASS_TOKEN not set")

    resp = requests.post(
        f"{base_url}/api/dev/fault",
        json={"kind": "exception"},
        headers=_headers(),
        timeout=10,
    )
    assert resp.status_code == 500
    payload = resp.json()
    assert payload.get("error")
    assert payload.get("message")
