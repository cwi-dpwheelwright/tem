import json
import os

import pytest
import requests


def _env(name: str):
    value = os.environ.get(name)
    return value if value else None


@pytest.mark.integration
def test_connectors_admin_run_and_queue():
    base_url = _env("CWI_CONNECTORS_ADMIN_URL")
    if not base_url:
        pytest.skip("CWI_CONNECTORS_ADMIN_URL not set")

    token = _env("CWI_CONNECTORS_ADMIN_TOKEN")
    headers = {"Authorization": f"Bearer {token}"} if token else {}
    status_resp = requests.get(f"{base_url}/admin/status", headers=headers, timeout=20)
    assert status_resp.status_code == 200
    status_payload = status_resp.json()

    payload = _env("CWI_CONNECTORS_RUN_PAYLOAD_JSON")
    run_payload = json.loads(payload) if payload else {}
    run_resp = requests.post(f"{base_url}/admin/run", json=run_payload, headers=headers, timeout=30)
    assert run_resp.status_code == 200

    expect_queue = _env("CWI_CONNECTORS_EXPECT_QUEUE") == "1"
    if expect_queue:
        after = requests.get(f"{base_url}/admin/status", headers=headers, timeout=20)
        assert after.status_code == 200
        after_payload = after.json()
        assert after_payload.get("queue_depth", 0) >= status_payload.get("queue_depth", 0)
