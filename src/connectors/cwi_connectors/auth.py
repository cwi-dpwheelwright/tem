from __future__ import annotations

import base64
import json
import time
from dataclasses import dataclass
from typing import Any, Dict
from urllib.parse import urlencode
from urllib.request import Request, urlopen

from .http_client import (
    DEFAULT_MAX_RESPONSE_BYTES,
    DEFAULT_TIMEOUT_SECONDS,
    fetch_json,
    read_limited,
    with_user_agent,
)
from .url_validation import validate_external_url

@dataclass
class OAuthToken:
    access_token: str
    expires_at: float


class TokenCache:
    def __init__(self) -> None:
        self._cache: Dict[str, OAuthToken] = {}

    def get(self, key: str) -> OAuthToken | None:
        token = self._cache.get(key)
        if not token:
            return None
        if token.expires_at <= time.time():
            return None
        return token

    def set(self, key: str, token: OAuthToken) -> None:
        self._cache[key] = token


class OAuthClient:
    def __init__(self, token_url: str, client_id: str, client_secret: str, scopes: list[str]) -> None:
        self.token_url = validate_external_url(token_url, "token_url")
        self.client_id = client_id
        self.client_secret = client_secret
        self.scopes = scopes
        self.cache = TokenCache()

    def get_token(self) -> str:
        cache_key = f"{self.client_id}:{self.token_url}:{' '.join(self.scopes)}"
        cached = self.cache.get(cache_key)
        if cached:
            return cached.access_token

        payload = urlencode(
            {
                "grant_type": "client_credentials",
                "client_id": self.client_id,
                "client_secret": self.client_secret,
                "scope": " ".join(self.scopes),
            }
        ).encode("utf-8")
        request = Request(
            self.token_url,
            data=payload,
            headers=with_user_agent({"Content-Type": "application/x-www-form-urlencoded"}),
            method="POST",
        )
        with urlopen(
            request,
            timeout=DEFAULT_TIMEOUT_SECONDS,
        ) as response:  # nosemgrep: python.lang.security.audit.dynamic-urllib-use-detected
            data = json.loads(read_limited(response, max_bytes=DEFAULT_MAX_RESPONSE_BYTES))
        access_token = data.get("access_token")
        expires_in = int(data.get("expires_in") or 3600)
        if not access_token:
            raise RuntimeError("oauth_token_missing")
        token = OAuthToken(access_token=access_token, expires_at=time.time() + expires_in - 30)
        self.cache.set(cache_key, token)
        return access_token


def get_json(
    url: str,
    headers: Dict[str, str] | None = None,
    timeout: float = DEFAULT_TIMEOUT_SECONDS,
    max_bytes: int = DEFAULT_MAX_RESPONSE_BYTES,
) -> Dict[str, Any]:
    validate_external_url(url, "url")
    return fetch_json(url, headers=headers or {}, timeout_seconds=timeout, max_bytes=max_bytes)


def build_auth_headers(auth: Dict[str, Any]) -> Dict[str, str]:
    auth_type = str(auth.get("type") or "").lower()
    if auth_type == "oauth2":
        oauth = auth.get("oauth2") or {}
        client = OAuthClient(
            token_url=str(oauth.get("token_url") or ""),
            client_id=str(oauth.get("client_id") or ""),
            client_secret=str(oauth.get("client_secret") or ""),
            scopes=list(oauth.get("scopes") or []),
        )
        token = client.get_token()
        return {"Authorization": f"Bearer {token}"}
    if auth_type == "api_key":
        api_key = auth.get("api_key") or {}
        header = str(api_key.get("header_name") or "Authorization")
        value = str(api_key.get("value") or "")
        if not value:
            return {}
        if header.lower() == "authorization" and not value.lower().startswith("bearer "):
            value = f"Bearer {value}"
        return {header: value}
    if auth_type == "basic":
        basic = auth.get("basic") or {}
        username = str(basic.get("username") or "")
        password = str(basic.get("password") or "")
        if not username or not password:
            return {}
        token = base64.b64encode(f"{username}:{password}".encode("utf-8")).decode("ascii")
        return {"Authorization": f"Basic {token}"}
    return {}
