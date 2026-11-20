import os
import time

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


def _tenant_param():
    tenant_id = os.environ.get("CWI_KILL_SWITCH_TENANT_ID")
    if not tenant_id:
        return {}
    return {"tenant_id": tenant_id}


def test_kill_switch_token_replay_protection(base_url):
    if not _truthy(os.environ.get("CWI_KILL_SWITCH_TOKEN_TEST")):
        pytest.skip("CWI_KILL_SWITCH_TOKEN_TEST not set")

    if not os.environ.get("CWI_BREAK_GLASS_TOKEN"):
        pytest.skip("CWI_BREAK_GLASS_TOKEN not set")

    kill_code = os.environ.get("CWI_KILL_SWITCH_CODE")
    if not kill_code:
        pytest.skip("CWI_KILL_SWITCH_CODE not set")

    issue = requests.post(
        f"{base_url}/api/admin/kill_switch/token",
        params=_tenant_param(),
        json={"kill_code": kill_code, "ttl_seconds": 30},
        headers=_headers(),
        timeout=10,
    )
    assert issue.status_code == 200
    token_id = issue.json().get("token_id")
    assert token_id

    enable = requests.post(
        f"{base_url}/api/admin/kill_switch",
        params=_tenant_param(),
        json={"enabled": True, "kill_code": kill_code, "token_id": token_id},
        headers=_headers(),
        timeout=10,
    )
    assert enable.status_code == 200

    replay = requests.post(
        f"{base_url}/api/admin/kill_switch",
        params=_tenant_param(),
        json={"enabled": False, "kill_code": kill_code, "token_id": token_id},
        headers=_headers(),
        timeout=10,
    )
    assert replay.status_code in (400, 403, 409)


def test_kill_switch_token_expiry(base_url):
    if not _truthy(os.environ.get("CWI_KILL_SWITCH_TOKEN_EXPIRE_TEST")):
        pytest.skip("CWI_KILL_SWITCH_TOKEN_EXPIRE_TEST not set")

    if not os.environ.get("CWI_BREAK_GLASS_TOKEN"):
        pytest.skip("CWI_BREAK_GLASS_TOKEN not set")

    kill_code = os.environ.get("CWI_KILL_SWITCH_CODE")
    if not kill_code:
        pytest.skip("CWI_KILL_SWITCH_CODE not set")

    ttl_seconds = int(os.environ.get("CWI_KILL_SWITCH_TOKEN_EXPIRE_SECONDS", "2"))

    issue = requests.post(
        f"{base_url}/api/admin/kill_switch/token",
        params=_tenant_param(),
        json={"kill_code": kill_code, "ttl_seconds": ttl_seconds},
        headers=_headers(),
        timeout=10,
    )
    assert issue.status_code == 200
    token_id = issue.json().get("token_id")
    assert token_id

    time.sleep(ttl_seconds + 1)

    expired = requests.post(
        f"{base_url}/api/admin/kill_switch",
        params=_tenant_param(),
        json={"enabled": True, "kill_code": kill_code, "token_id": token_id},
        headers=_headers(),
        timeout=10,
    )
    assert expired.status_code in (400, 403, 410)


def test_kill_switch_irreversible_blocks_disable(base_url):
    if not _truthy(os.environ.get("CWI_KILL_SWITCH_IRREVERSIBLE_TEST")):
        pytest.skip("CWI_KILL_SWITCH_IRREVERSIBLE_TEST not set")

    if not os.environ.get("CWI_BREAK_GLASS_TOKEN"):
        pytest.skip("CWI_BREAK_GLASS_TOKEN not set")

    kill_code = os.environ.get("CWI_KILL_SWITCH_CODE")
    if not kill_code:
        pytest.skip("CWI_KILL_SWITCH_CODE not set")

    enable = requests.post(
        f"{base_url}/api/admin/kill_switch",
        params=_tenant_param(),
        json={"enabled": True, "kill_code": kill_code, "irreversible": True},
        headers=_headers(),
        timeout=10,
    )
    assert enable.status_code == 200

    disable = requests.post(
        f"{base_url}/api/admin/kill_switch",
        params=_tenant_param(),
        json={"enabled": False, "kill_code": kill_code},
        headers=_headers(),
        timeout=10,
    )
    assert disable.status_code in (403, 409)
