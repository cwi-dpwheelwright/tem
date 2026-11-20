from __future__ import annotations

import json
import threading
from datetime import datetime, timedelta, timezone
from typing import Callable
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

from .config import EdgeAgentConfig, IdentityAuthConfig


class IdentityTokenProvider:
    def __init__(self, config: IdentityAuthConfig, timeout_seconds: float = 10.0) -> None:
        self._config = config
        self._timeout_seconds = timeout_seconds
        self._lock = threading.Lock()
        self._access_token: str | None = None
        self._refresh_token: str | None = None
        self._expires_utc: datetime | None = None

    def get_token(self) -> str:
        with self._lock:
            if self._has_valid_token():
                return self._access_token or ""

            if self._refresh_token:
                try:
                    self._refresh()
                except Exception:
                    self._clear_tokens()

            if not self._has_valid_token():
                self._login()

            if not self._access_token:
                raise RuntimeError("identity login did not return an access token")

            return self._access_token

    def _has_valid_token(self) -> bool:
        if not self._access_token or not self._expires_utc:
            return False
        refresh_deadline = self._expires_utc - timedelta(seconds=self._config.refresh_margin_seconds)
        return datetime.now(timezone.utc) < refresh_deadline

    def _login(self) -> None:
        access_token, refresh_token, expires_utc = _request_token(
            self._config.login_url,
            {
                "tenant_id": self._config.tenant_id,
                "username": self._config.username,
                "password": self._config.password,
            },
            timeout_seconds=self._timeout_seconds,
        )
        self._store_tokens(access_token, refresh_token, expires_utc)

    def _refresh(self) -> None:
        if not self._refresh_token:
            raise RuntimeError("refresh token missing")
        access_token, refresh_token, expires_utc = _request_token(
            self._config.refresh_url,
            {
                "refresh_token": self._refresh_token,
            },
            timeout_seconds=self._timeout_seconds,
        )
        self._store_tokens(access_token, refresh_token, expires_utc)

    def _store_tokens(self, access_token: str, refresh_token: str | None, expires_utc: datetime) -> None:
        self._access_token = access_token
        self._refresh_token = refresh_token
        self._expires_utc = expires_utc

    def _clear_tokens(self) -> None:
        self._access_token = None
        self._refresh_token = None
        self._expires_utc = None


def build_token_provider(config: EdgeAgentConfig) -> Callable[[], str]:
    provider = IdentityTokenProvider(config.identity_auth, timeout_seconds=config.ingest_timeout_seconds)
    return provider.get_token


def _request_token(url: str, payload: dict[str, str], timeout_seconds: float) -> tuple[str, str | None, datetime]:
    body = json.dumps(payload).encode("utf-8")
    request = Request(
        url,
        data=body,
        headers={"Content-Type": "application/json"},
        method="POST",
    )

    try:
        with urlopen(request, timeout=timeout_seconds) as response:
            response_body = response.read().decode("utf-8")
    except HTTPError as exc:
        details = exc.read().decode("utf-8", "replace")
        raise RuntimeError(f"identity token request failed ({exc.code}): {details}") from exc
    except URLError as exc:
        raise RuntimeError(f"identity token request failed: {exc}") from exc

    try:
        parsed = json.loads(response_body)
    except json.JSONDecodeError as exc:
        raise RuntimeError("identity token response was not valid JSON") from exc

    access_token = parsed.get("access_token")
    if not isinstance(access_token, str) or not access_token.strip():
        raise RuntimeError("identity token response missing access_token")

    refresh_token = parsed.get("refresh_token")
    if refresh_token is not None and not isinstance(refresh_token, str):
        raise RuntimeError("identity token response refresh_token must be a string")

    expires_utc = _parse_expires_utc(parsed.get("expires_utc"))
    return access_token.strip(), refresh_token.strip() if isinstance(refresh_token, str) else None, expires_utc


def _parse_expires_utc(value: object) -> datetime:
    if not isinstance(value, str) or not value.strip():
        return datetime.now(timezone.utc) + timedelta(minutes=5)

    normalized = value.strip().replace("Z", "+00:00")
    try:
        parsed = datetime.fromisoformat(normalized)
    except ValueError as exc:
        raise RuntimeError(f"identity token response expires_utc was invalid: {value}") from exc

    if parsed.tzinfo is None:
        return parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)
