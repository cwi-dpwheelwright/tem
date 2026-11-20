import os

import pytest
import requests


pytestmark = pytest.mark.integration


def _headers(include_break_glass: bool = True):
    headers = {}
    token = os.environ.get("CWI_AUTH_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    if include_break_glass:
        break_glass = os.environ.get("CWI_BREAK_GLASS_TOKEN")
        if break_glass:
            headers["X-Break-Glass-Token"] = break_glass
    return headers


def test_pack_activation_requires_break_glass(base_url):
    if os.environ.get("CWI_PACK_ACTIVATION_REQUIRED_TEST") != "1":
        pytest.skip("CWI_PACK_ACTIVATION_REQUIRED_TEST not set")

    tenant_id = os.environ.get("CWI_PACK_TENANT_ID", "tenant-1")
    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
    pack_hash = os.environ.get("CWI_PACK_HASH", "demo-hash")

    response = requests.post(
        f"{base_url}/api/packs/activation",
        json={
            "tenant_id": tenant_id,
            "pack_name": pack_name,
            "pack_hash": pack_hash,
            "actor_user_id": "pack-admin-test",
        },
        headers=_headers(include_break_glass=False),
        timeout=15,
    )
    assert response.status_code in (401, 403)


def test_pack_activation_and_rollback(base_url):
    if os.environ.get("CWI_PACK_ACTIVATION_TEST") != "1":
        pytest.skip("CWI_PACK_ACTIVATION_TEST not set")

    if not os.environ.get("CWI_BREAK_GLASS_TOKEN"):
        pytest.skip("CWI_BREAK_GLASS_TOKEN not set")

    tenant_id = os.environ.get("CWI_PACK_TENANT_ID", "tenant-1")
    pack_name = os.environ.get("CWI_PACK_NAME", "enterprise")
    pack_hash = os.environ.get("CWI_PACK_HASH", "demo-hash")

    activate = requests.post(
        f"{base_url}/api/packs/activation",
        json={
            "tenant_id": tenant_id,
            "pack_name": pack_name,
            "pack_hash": pack_hash,
            "actor_user_id": "pack-admin-test",
        },
        headers=_headers(),
        timeout=15,
    )
    assert activate.status_code == 200

    rollback = requests.post(
        f"{base_url}/api/packs/rollback",
        json={
            "tenant_id": tenant_id,
            "pack_name": pack_name,
            "actor_user_id": "pack-admin-test",
            "reason": "gold-test",
        },
        headers=_headers(),
        timeout=15,
    )
    assert rollback.status_code == 200
