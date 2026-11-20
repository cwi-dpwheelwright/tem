from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any, Callable, Dict
from urllib.request import Request, urlopen
from urllib.error import URLError, HTTPError

from .revocation import ensure_cert_not_revoked
from .transport import build_ssl_context, verify_pinned_fingerprint


@dataclass
class SurvivalSignalResult:
    ok: bool
    status_code: int | None
    error: str | None


class SurvivalSignalClient:
    def __init__(
        self,
        endpoint_url: str,
        token_provider: Callable[[], str] | None = None,
        mtls_cert_path: str | None = None,
        mtls_key_path: str | None = None,
        revoked_serials_path: str | None = None,
        ca_bundle_path: str | None = None,
        crl_url: str | None = None,
        crl_cache_path: str | None = None,
        crl_refresh_seconds: int | None = None,
        pinned_fingerprints: tuple[str, ...] = tuple(),
        verify_hostname: bool = True,
        timeout_seconds: float = 10.0,
    ) -> None:
        self.endpoint_url = endpoint_url
        self._token_provider = token_provider or (lambda: "")
        self.mtls_cert_path = mtls_cert_path
        self.mtls_key_path = mtls_key_path
        self.revoked_serials_path = revoked_serials_path
        self.ca_bundle_path = ca_bundle_path
        self.crl_url = crl_url
        self.crl_cache_path = crl_cache_path
        self.crl_refresh_seconds = crl_refresh_seconds
        self.pinned_fingerprints = pinned_fingerprints
        self.verify_hostname = verify_hostname
        self.timeout_seconds = timeout_seconds

    def send_signal(self, payload: Dict[str, Any]) -> SurvivalSignalResult:
        body = json.dumps(payload).encode("utf-8")
        headers = {
            "Content-Type": "application/json",
        }
        token = self._token_provider().strip()
        if token:
            headers["Authorization"] = f"Bearer {token}"
        request = Request(
            self.endpoint_url,
            data=body,
            headers=headers,
            method="POST",
        )
        try:
            ensure_cert_not_revoked(
                self.mtls_cert_path,
                self.revoked_serials_path,
                crl_url=self.crl_url,
                crl_cache_path=self.crl_cache_path,
                crl_refresh_seconds=self.crl_refresh_seconds,
            )
            ssl_context = build_ssl_context(
                self.mtls_cert_path,
                self.mtls_key_path,
                self.ca_bundle_path,
                verify_hostname=self.verify_hostname,
            )
            with urlopen(request, timeout=self.timeout_seconds, context=ssl_context) as response:
                verify_pinned_fingerprint(response, self.pinned_fingerprints)
                return SurvivalSignalResult(ok=True, status_code=response.status, error=None)
        except HTTPError as exc:
            return SurvivalSignalResult(ok=False, status_code=exc.code, error=str(exc))
        except URLError as exc:
            return SurvivalSignalResult(ok=False, status_code=None, error=str(exc))
