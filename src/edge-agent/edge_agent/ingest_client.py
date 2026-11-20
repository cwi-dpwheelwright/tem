from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any, Callable, Dict
from urllib.request import Request, urlopen
from urllib.error import URLError, HTTPError
from time import perf_counter

from .backoff import BackoffPolicy, CircuitBreaker
from .observability import Observability, NoopObservability
from .revocation import ensure_cert_not_revoked
from .transport import build_ssl_context, verify_pinned_fingerprint


@dataclass
class IngestResult:
    ok: bool
    status_code: int | None
    error: str | None


class TelemetryIngestClient:
    def __init__(
        self,
        base_url: str,
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
        backoff: BackoffPolicy | None = None,
        breaker: CircuitBreaker | None = None,
        timeout_seconds: float = 10.0,
        observability: Observability | None = None,
    ) -> None:
        self.base_url = base_url.rstrip("/")
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
        self.backoff = backoff or BackoffPolicy()
        self.breaker = breaker or CircuitBreaker()
        self.timeout_seconds = timeout_seconds
        self.observability = observability or NoopObservability()
        self.last_error: str | None = None
        self.last_error_utc: float | None = None

    def ingest(self, payload: Dict[str, Any]) -> IngestResult:
        if not self.breaker.allow_request():
            self.observability.record_ingest(False, None, 0.0)
            return IngestResult(ok=False, status_code=None, error="circuit_open")

        body = json.dumps(payload).encode("utf-8")
        headers = {
            "Content-Type": "application/json",
        }
        token = self._token_provider().strip()
        if token:
            headers["Authorization"] = f"Bearer {token}"
        request = Request(
            f"{self.base_url}/api/ingest/telemetry",
            data=body,
            headers=headers,
            method="POST",
        )
        try:
            span_attrs = {
                "tenant_id": payload.get("tenant_id", ""),
                "signal_count": len(payload.get("signals", [])) if isinstance(payload, dict) else 0,
            }
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
            start = perf_counter()
            with self.observability.start_span("edge_agent.ingest", span_attrs):
                with urlopen(request, timeout=self.timeout_seconds, context=ssl_context) as response:
                    verify_pinned_fingerprint(response, self.pinned_fingerprints)
                    self.breaker.record_success()
                    duration_ms = max(0.0, (perf_counter() - start) * 1000.0)
                    self.observability.record_ingest(True, response.status, duration_ms)
                    self.last_error = None
                    self.last_error_utc = None
                    return IngestResult(ok=True, status_code=response.status, error=None)
        except HTTPError as exc:
            self.breaker.record_failure()
            duration_ms = max(0.0, (perf_counter() - start) * 1000.0) if "start" in locals() else 0.0
            self.observability.record_ingest(False, exc.code, duration_ms)
            self.last_error = str(exc)
            self.last_error_utc = perf_counter()
            return IngestResult(ok=False, status_code=exc.code, error=str(exc))
        except URLError as exc:
            self.breaker.record_failure()
            duration_ms = max(0.0, (perf_counter() - start) * 1000.0) if "start" in locals() else 0.0
            self.observability.record_ingest(False, None, duration_ms)
            self.last_error = str(exc)
            self.last_error_utc = perf_counter()
            return IngestResult(ok=False, status_code=None, error=str(exc))
