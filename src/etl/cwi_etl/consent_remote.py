from __future__ import annotations

import json
import os
from dataclasses import dataclass
from typing import Any, Dict
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen

from cwi_etl.operator_memory import ConsentMissingError, ConsentRecord
from cwi_etl.url_validation import validate_http_url


@dataclass(frozen=True)
class RemoteConsentConfig:
    base_url: str
    path: str = "/api/consent/latest"
    token: str | None = None
    timeout_seconds: float = 5.0

    @staticmethod
    def from_env(prefix: str = "CWI_CONSENT_REMOTE") -> "RemoteConsentConfig":
        base_url = os.environ.get(f"{prefix}_BASE_URL", "").strip()
        if not base_url:
            raise ConsentMissingError(f"{prefix}_BASE_URL is required for remote consent")
        validate_http_url(base_url, f"{prefix}_BASE_URL")
        path = os.environ.get(f"{prefix}_PATH", "/api/consent/latest").strip() or "/api/consent/latest"
        token = os.environ.get(f"{prefix}_TOKEN", "").strip() or None
        timeout_raw = os.environ.get(f"{prefix}_TIMEOUT_SECONDS", "5").strip() or "5"
        try:
            timeout = float(timeout_raw)
        except ValueError as exc:
            raise ConsentMissingError(f"{prefix}_TIMEOUT_SECONDS must be numeric") from exc
        return RemoteConsentConfig(
            base_url=base_url,
            path=path,
            token=token,
            timeout_seconds=timeout,
        )


class RemoteConsentClient:
    def __init__(self, config: RemoteConsentConfig) -> None:
        self._config = config

    def get_latest_consent(
        self,
        *,
        tenant_id: str,
        operator_id: str,
        observed_utc: str | None = None,
    ) -> ConsentRecord | None:
        params = {"tenant_id": tenant_id, "operator_id": operator_id}
        if observed_utc:
            params["observed_utc"] = observed_utc
        url = f"{self._config.base_url.rstrip('/')}{self._config.path}?{urlencode(params)}"
        headers = {"Accept": "application/json"}
        if self._config.token:
            headers["Authorization"] = f"Bearer {self._config.token}"
        request = Request(url, headers=headers, method="GET")
        try:
            with urlopen(request, timeout=self._config.timeout_seconds) as response:  # nosemgrep: python.lang.security.audit.dynamic-urllib-use-detected
                if response.status == 404:
                    return None
                if response.status >= 400:
                    raise ConsentMissingError(f"remote consent fetch failed with status {response.status}")
                payload = json.loads(response.read().decode("utf-8"))
        except HTTPError as exc:
            if exc.code == 404:
                return None
            raise ConsentMissingError(f"remote consent fetch failed with status {exc.code}") from exc
        except URLError as exc:
            raise ConsentMissingError(f"remote consent fetch failed: {exc}") from exc

        return _parse_consent_payload(payload)

    def get_consent_or_raise(
        self,
        *,
        tenant_id: str,
        operator_id: str,
        observed_utc: str | None = None,
    ) -> ConsentRecord:
        consent = self.get_latest_consent(
            tenant_id=tenant_id,
            operator_id=operator_id,
            observed_utc=observed_utc,
        )
        if consent is None:
            raise ConsentMissingError("no consent record found for operator")
        return consent


def _parse_consent_payload(payload: Dict[str, Any]) -> ConsentRecord:
    if "consent" in payload and isinstance(payload["consent"], dict):
        payload = payload["consent"]
    granted = payload.get("granted")
    if granted is None and "consent_granted" in payload:
        granted = payload.get("consent_granted")
    granted_utc = payload.get("granted_utc")
    if granted_utc is None and "consent_granted_utc" in payload:
        granted_utc = payload.get("consent_granted_utc")
    if granted_utc is None and "grantedUtc" in payload:
        granted_utc = payload.get("grantedUtc")
    if granted is None or granted_utc is None:
        raise ConsentMissingError("remote consent payload missing granted/granted_utc")
    source = payload.get("source") or payload.get("consent_source")
    return ConsentRecord(granted=bool(granted), granted_utc=str(granted_utc), source=source)
