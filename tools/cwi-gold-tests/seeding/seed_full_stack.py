import argparse
import json
import os
import sys
from typing import Any, Dict, List, Optional

import requests


def _truthy(value: Optional[str]) -> bool:
    return str(value or "").lower() in ("1", "true", "yes")


def _load_config(path: Optional[str]) -> Dict[str, Any]:
    if not path:
        return {}
    with open(path, "r", encoding="utf-8") as handle:
        return json.load(handle)


def _request(method: str, url: str, token: Optional[str] = None, **kwargs) -> requests.Response:
    headers = kwargs.pop("headers", {})
    if token:
        headers = dict(headers)
        headers["Authorization"] = f"Bearer {token}"
    return requests.request(method, url, headers=headers, timeout=20, **kwargs)


def _identity_login(base_url: str, payload: Dict[str, Any]) -> str:
    resp = requests.post(f"{base_url}/auth/login", json=payload, timeout=20)
    resp.raise_for_status()
    token = (resp.json() or {}).get("access_token")
    if not token:
        raise RuntimeError("Identity login did not return access_token")
    return token


def _ensure_tenant(base_url: str, token: str, tenant_id: str, name: str) -> None:
    resp = _request("POST", f"{base_url}/admin/tenants", token, json={"tenant_id": tenant_id, "name": name})
    if resp.status_code in (200, 409):
        return
    resp.raise_for_status()


def _invite_user(base_url: str, token: str, tenant_id: str, user: Dict[str, Any]) -> Optional[str]:
    payload = {
        "tenant_id": tenant_id,
        "email": user["email"],
        "username": user.get("username"),
        "display_name": user.get("display_name") or user.get("email"),
    }
    resp = _request("POST", f"{base_url}/admin/users/invite", token, json=payload)
    if resp.status_code == 409:
        return None
    resp.raise_for_status()
    return (resp.json() or {}).get("invite_token")


def _activate_user(base_url: str, invite_token: str, password: str) -> None:
    payload = {"token": invite_token, "password": password}
    resp = requests.post(f"{base_url}/auth/activate", json=payload, timeout=20)
    if resp.status_code in (200, 401, 403):
        return
    resp.raise_for_status()


def _find_user_id(base_url: str, token: str, tenant_id: str, email: str) -> Optional[str]:
    resp = _request("GET", f"{base_url}/admin/users", token, params={"tenant_id": tenant_id})
    resp.raise_for_status()
    for item in resp.json() or []:
        if item.get("email") == email:
            return item.get("user_id")
    return None


def _assign_roles(base_url: str, token: str, tenant_id: str, user_id: str, roles: List[str]) -> None:
    payload = {"tenant_id": tenant_id, "roles": roles}
    resp = _request("PUT", f"{base_url}/admin/users/{user_id}/roles", token, json=payload)
    if resp.status_code in (200, 404):
        return
    resp.raise_for_status()


def _seed_pack_activation(base_url: str, token: str, activation: Dict[str, Any]) -> None:
    resp = _request("POST", f"{base_url}/api/packs/activation", token, json=activation)
    if resp.status_code in (200, 409):
        return
    resp.raise_for_status()


def _seed_documents(ingest_url: str, payloads: List[Dict[str, Any]]) -> None:
    for payload in payloads:
        resp = requests.post(ingest_url, json=payload, timeout=30)
        if resp.status_code >= 400:
            resp.raise_for_status()


def main() -> int:
    parser = argparse.ArgumentParser(description="Seed identity tenants, packs, and documents for E2E gold tests")
    parser.add_argument("--config", help="Path to seed_config.json", default=os.environ.get("CWI_SEED_CONFIG"))
    args = parser.parse_args()

    config = _load_config(args.config)

    identity_cfg = config.get("identity") or {}
    identity_url = identity_cfg.get("base_url") or os.environ.get("CWI_IDENTITY_BASE_URL") or os.environ.get("IDENTITY_BASE_URL")
    identity_url = (identity_url or "").rstrip("/")

    token = None
    if identity_url:
        admin_email = identity_cfg.get("admin_email") or os.environ.get("IDENTITY_BOOTSTRAP_ADMIN_EMAIL")
        admin_username = identity_cfg.get("admin_username") or os.environ.get("IDENTITY_BOOTSTRAP_ADMIN_USERNAME")
        admin_password = identity_cfg.get("admin_password") or os.environ.get("IDENTITY_BOOTSTRAP_ADMIN_PASSWORD")
        tenant_id = identity_cfg.get("admin_tenant_id") or os.environ.get("IDENTITY_BOOTSTRAP_TENANT_ID") or "tenant-1"
        if admin_password and (admin_email or admin_username):
            payload = {"tenant_id": tenant_id, "password": admin_password}
            if admin_email:
                payload["email"] = admin_email
            if admin_username:
                payload["username"] = admin_username
            token = _identity_login(identity_url, payload)
        else:
            print("Identity admin credentials not configured; skipping identity seeding.")

    if token and identity_cfg.get("tenants"):
        for tenant in identity_cfg["tenants"]:
            tenant_id = tenant["tenant_id"]
            name = tenant.get("name", tenant_id)
            _ensure_tenant(identity_url, token, tenant_id, name)

            for user in tenant.get("users", []):
                invite_token = _invite_user(identity_url, token, tenant_id, user)
                password = user.get("password")
                if invite_token and password:
                    _activate_user(identity_url, invite_token, password)

                roles = user.get("roles") or []
                if roles:
                    user_id = _find_user_id(identity_url, token, tenant_id, user["email"])
                    if user_id:
                        _assign_roles(identity_url, token, tenant_id, user_id, roles)

    packs_cfg = config.get("packs") or {}
    packs_base = packs_cfg.get("base_url") or os.environ.get("CWI_BASE_URL")
    if packs_base and token and packs_cfg.get("activations"):
        for activation in packs_cfg["activations"]:
            _seed_pack_activation(packs_base.rstrip("/"), token, activation)

    docs_cfg = config.get("documents") or {}
    ingest_url = docs_cfg.get("ingest_url") or os.environ.get("CWI_ETL_INGEST_URL")
    payloads = docs_cfg.get("payloads") or []
    if ingest_url and payloads:
        _seed_documents(ingest_url, payloads)

    print("Seeding completed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
