import os
import pytest
import requests


def pytest_sessionstart(session):
    if not os.environ.get("CWI_BASE_URL") and os.environ.get("AUTH_REQUIRED") is None:
        return
    _ensure_auth_token()


@pytest.fixture(scope="session")
def base_url():
    url = os.environ.get("CWI_BASE_URL")
    if not url:
        pytest.skip("CWI_BASE_URL not set")
    return url.rstrip("/")


def _ensure_auth_token():
    if os.environ.get("CWI_AUTH_TOKEN"):
        return

    auth_raw = os.environ.get("AUTH_REQUIRED")
    auth_required = True if auth_raw is None or not auth_raw.strip() else auth_raw.lower() in ("1", "true", "yes")
    if not auth_required:
        return

    tenant_id = os.environ.get("IDENTITY_BOOTSTRAP_TENANT_ID") or os.environ.get("CWI_TENANT_ID") or "tenant-1"
    email = os.environ.get("IDENTITY_BOOTSTRAP_ADMIN_EMAIL")
    username = os.environ.get("IDENTITY_BOOTSTRAP_ADMIN_USERNAME")
    password = os.environ.get("IDENTITY_BOOTSTRAP_ADMIN_PASSWORD")
    if not password:
        raise RuntimeError("IDENTITY_BOOTSTRAP_ADMIN_PASSWORD is required to fetch CWI_AUTH_TOKEN.")

    payload = {"tenant_id": tenant_id, "password": password}
    if email:
        payload["email"] = email
    elif username:
        payload["username"] = username
    else:
        raise RuntimeError("IDENTITY_BOOTSTRAP_ADMIN_EMAIL or IDENTITY_BOOTSTRAP_ADMIN_USERNAME is required.")

    errors = []
    for identity_base in _candidate_identity_urls():
        url = f"{identity_base.rstrip('/')}/auth/login"
        try:
            resp = requests.post(url, json=payload, timeout=10)
        except requests.RequestException as exc:
            errors.append(f"{url} -> {type(exc).__name__}")
            continue

        if resp.status_code != 200:
            snippet = resp.text[:200].replace("\n", " ")
            errors.append(f"{url} -> {resp.status_code} {snippet}")
            continue

        token = (resp.json() or {}).get("access_token")
        if not token:
            errors.append(f"{url} -> missing access_token")
            continue

        os.environ["CWI_AUTH_TOKEN"] = token
        return

    error_text = "; ".join(errors) if errors else "no identity endpoints attempted"
    raise RuntimeError(f"Failed to fetch CWI_AUTH_TOKEN. Attempts: {error_text}")


def _candidate_identity_urls():
    urls = []
    port = os.environ.get("IDENTITY_HTTP_PORT")
    if port:
        urls.append(f"http://localhost:{port}")

    for name in ("CWI_IDENTITY_BASE_URL", "IDENTITY_BASE_URL", "IDENTITY_URL"):
        value = os.environ.get(name)
        if value:
            urls.append(value)

    base_url = os.environ.get("CWI_BASE_URL")
    if base_url:
        urls.append(base_url)

    seen = set()
    ordered = []
    for url in urls:
        normalized = url.rstrip("/")
        if normalized and normalized not in seen:
            ordered.append(normalized)
            seen.add(normalized)
    return ordered
