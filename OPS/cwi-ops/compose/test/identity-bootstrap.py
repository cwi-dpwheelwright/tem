import json
import os
import time
import urllib.error
import urllib.request


def _env(name: str) -> str | None:
    value = os.environ.get(name)
    return value.strip() if value else None


def _request(method: str, url: str, payload: dict | None = None):
    data = None
    headers = {}
    if payload is not None:
        data = json.dumps(payload).encode("utf-8")
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            body = resp.read().decode("utf-8") if resp.read else ""
            return resp.status, body
    except urllib.error.HTTPError as exc:
        return exc.code, exc.read().decode("utf-8")
    except urllib.error.URLError as exc:
        return None, str(exc)


def _get_json(url: str):
    status, body = _request("GET", url)
    if status != 200:
        return status, body
    try:
        return status, json.loads(body)
    except json.JSONDecodeError:
        return status, body


def _post_json(url: str, payload: dict):
    return _request("POST", url, payload)


def _wait_for_ready(base_url: str, attempts: int = 60, delay: float = 2.0) -> bool:
    for _ in range(attempts):
        status, _ = _request("GET", f"{base_url}/healthz")
        if status == 200:
            return True
        time.sleep(delay)
    return False


def main() -> int:
    tenant_id = _env("IDENTITY_BOOTSTRAP_TENANT_ID")
    email = _env("IDENTITY_BOOTSTRAP_ADMIN_EMAIL")
    username = _env("IDENTITY_BOOTSTRAP_ADMIN_USERNAME")
    password = _env("IDENTITY_BOOTSTRAP_ADMIN_PASSWORD")
    display_name = _env("IDENTITY_BOOTSTRAP_ADMIN_NAME") or "Admin"
    base_url = _env("IDENTITY_BOOTSTRAP_URL") or "http://cwi-identity-server:8085"

    if not (tenant_id and email and password):
        print("Bootstrap config incomplete; skipping identity bootstrap.")
        return 0

    if not _wait_for_ready(base_url):
        print("Identity server not ready; aborting bootstrap.")
        return 1

    tenant_payload = {"tenant_id": tenant_id, "name": tenant_id}
    status, body = _post_json(f"{base_url}/admin/tenants", tenant_payload)
    if status not in (200, 201, 409):
        print(f"Tenant bootstrap failed ({status}): {body}")
        return 1

    invite_payload = {
        "tenant_id": tenant_id,
        "email": email,
        "username": username,
        "display_name": display_name,
    }
    status, body = _post_json(f"{base_url}/admin/users/invite", invite_payload)
    invite_token = None
    if status in (200, 201):
        try:
            invite_token = json.loads(body).get("invite_token")
        except json.JSONDecodeError:
            invite_token = None
    elif status != 409:
        print(f"User invite failed ({status}): {body}")
        return 1

    status, users = _get_json(f"{base_url}/admin/users?tenant_id={tenant_id}")
    if status != 200 or not isinstance(users, list):
        print(f"User list failed ({status}): {users}")
        return 1

    user = None
    for entry in users:
        if entry.get("email") == email or (username and entry.get("username") == username):
            user = entry
            break

    if not user:
        print("Bootstrap user not found after invite.")
        return 1

    user_id = user.get("user_id")
    if not user_id:
        print("Bootstrap user missing user_id.")
        return 1

    if invite_token:
        approve_payload = {"tenant_id": tenant_id}
        status, body = _post_json(f"{base_url}/admin/users/{user_id}/approve", approve_payload)
        if status not in (200, 204, 409, 404):
            print(f"User approve failed ({status}): {body}")
            return 1

        activate_payload = {"token": invite_token, "password": password}
        status, body = _post_json(f"{base_url}/auth/activate", activate_payload)
        if status not in (200, 204):
            print(f"User activate failed ({status}): {body}")
            return 1

    print("Identity bootstrap complete.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
