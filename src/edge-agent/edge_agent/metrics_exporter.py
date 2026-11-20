from __future__ import annotations

import json
import time
from dataclasses import dataclass
from time import perf_counter
from typing import Any, Callable, Dict, Iterable
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

from .backoff import BackoffPolicy, CircuitBreaker
from .buffer import BufferItem, JsonlBuffer
from .metrics import LinkMetricsInterface, LinkMetricsTransportRequest
from .observability import Observability, NoopObservability
from .revocation import ensure_cert_not_revoked
from .transport import build_ssl_context, verify_pinned_fingerprint


@dataclass(frozen=True)
class MetricsExporterConfig:
    endpoints: tuple[str, ...]
    node_id: str
    queue_path: str
    queue_max_items: int | None
    queue_max_bytes: int | None
    queue_drop_policy: str
    timeout_seconds: float
    max_batch: int
    backoff_base_seconds: float
    backoff_max_seconds: float
    failure_threshold: int
    recovery_seconds: float


class MetricsExporter:
    def __init__(
        self,
        config: MetricsExporterConfig,
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
        observability: Observability | None = None,
    ) -> None:
        self.config = config
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
        self.buffer = JsonlBuffer(
            config.queue_path,
            max_items=config.queue_max_items,
            max_bytes=config.queue_max_bytes,
            drop_policy=config.queue_drop_policy,
        )
        self.backoff = BackoffPolicy(
            base_seconds=config.backoff_base_seconds,
            max_seconds=config.backoff_max_seconds,
        )
        self.breaker = CircuitBreaker(
            failure_threshold=config.failure_threshold,
            recovery_seconds=config.recovery_seconds,
        )
        self.observability = observability or NoopObservability()
        self._sequence = 0
        self._retry_attempt = 0
        self._next_retry_at = 0.0

    def enqueue(self, interfaces: Iterable[LinkMetricsInterface], observed_utc: str) -> bool:
        self._sequence += 1
        payload = LinkMetricsTransportRequest(
            node_id=self.config.node_id,
            interfaces=list(interfaces),
            observed_utc=observed_utc,
            sequence=self._sequence,
        ).to_dict()
        item_id = f"{self.config.node_id}:{observed_utc}:{self._sequence}"
        return self.buffer.enqueue(BufferItem(item_id=item_id, payload=payload))

    def flush(self) -> None:
        now = time.time()
        if now < self._next_retry_at:
            self.observability.record_export_delay("link_metrics", self._next_retry_at - now)
            return
        while True:
            pending = self.buffer.pending(limit=self.config.max_batch)
            if not pending:
                return
            sent = 0
            for item in pending:
                if self._send_payload(item.payload):
                    sent += 1
                    continue
                break
            if sent:
                self.buffer.ack(sent)
            if sent < len(pending):
                return

    def _send_payload(self, payload: Dict[str, Any]) -> bool:
        if not self.breaker.allow_request():
            self.observability.record_export(False, None, 0.0)
            self._next_retry_at = max(
                self._next_retry_at,
                time.time() + self.breaker.recovery_seconds,
            )
            return False
        body = json.dumps(payload).encode("utf-8")
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
        ok = True
        for endpoint in self.config.endpoints:
            url = _resolve_endpoint(endpoint)
            headers = {
                "Content-Type": "application/json",
            }
            token = self._token_provider().strip()
            if token:
                headers["Authorization"] = f"Bearer {token}"
            request = Request(
                url,
                data=body,
                headers=headers,
                method="POST",
            )
            start = perf_counter()
            try:
                with self.observability.start_span("edge_agent.metrics_export"):
                    with urlopen(request, timeout=self.config.timeout_seconds, context=ssl_context) as response:
                        verify_pinned_fingerprint(response, self.pinned_fingerprints)
                        duration_ms = max(0.0, (perf_counter() - start) * 1000.0)
                        self.observability.record_export(True, response.status, duration_ms)
            except HTTPError as exc:
                duration_ms = max(0.0, (perf_counter() - start) * 1000.0)
                self.observability.record_export(False, exc.code, duration_ms)
                ok = False
                break
            except URLError as exc:
                duration_ms = max(0.0, (perf_counter() - start) * 1000.0)
                self.observability.record_export(False, None, duration_ms)
                ok = False
                break
        if ok:
            self.breaker.record_success()
            self._retry_attempt = 0
            self._next_retry_at = 0.0
        else:
            self.breaker.record_failure()
            delay = self.backoff.next_delay(self._retry_attempt)
            self._retry_attempt += 1
            if delay > 0:
                self.observability.record_export_delay("link_metrics", delay)
            self._next_retry_at = time.time() + delay
        return ok


def _resolve_endpoint(endpoint: str) -> str:
    trimmed = endpoint.rstrip("/")
    if "/api/transport/link-metrics" in trimmed:
        return trimmed
    return f"{trimmed}/api/transport/link-metrics"
