from __future__ import annotations

import time
from dataclasses import dataclass
from typing import Any, Dict, List
from urllib.error import HTTPError, URLError

from .http_client import post_json
from .delivery_queue import DeliveryQueue, QueueItem
from .state_store import utc_now
from .url_validation import validate_http_url


@dataclass(frozen=True)
class ProvenanceSummary:
    source: str
    doc_ids: List[str]
    chunk_ids: List[str]
    redaction_count: int

    def to_payload(self) -> Dict[str, Any]:
        return {
            "source": self.source,
            "doc_ids": self.doc_ids,
            "chunk_ids": self.chunk_ids,
            "redaction_count": self.redaction_count,
        }


@dataclass(frozen=True)
class ProvenanceEnvelope:
    version: str
    envelope_id: str
    emitted_utc: str
    connector_id: str
    source: str
    summary: ProvenanceSummary
    tenant_id: str | None = None
    workspace_id: str | None = None
    run_id: str | None = None

    def to_payload(self) -> Dict[str, Any]:
        payload = {
            "version": self.version,
            "envelope_id": self.envelope_id,
            "emitted_utc": self.emitted_utc,
            "connector_id": self.connector_id,
            "source": self.source,
            "summary": self.summary.to_payload(),
        }
        if self.tenant_id:
            payload["tenant_id"] = self.tenant_id
        if self.workspace_id:
            payload["workspace_id"] = self.workspace_id
        if self.run_id:
            payload["run_id"] = self.run_id
        return payload


class ProvenanceClient:
    def __init__(
        self,
        base_url: str,
        jwt: str,
        queue: DeliveryQueue | None = None,
        timeout_seconds: float = 20.0,
        max_attempts: int = 3,
        backoff_seconds: float = 1.0,
    ) -> None:
        self.base_url = validate_http_url(base_url, "base_url").rstrip("/")
        self.jwt = jwt
        self.queue = queue
        self.timeout_seconds = timeout_seconds
        self.max_attempts = max_attempts
        self.backoff_seconds = backoff_seconds

    def emit(self, envelope: ProvenanceEnvelope) -> bool:
        self.flush_queue()
        try:
            self._post_with_retry(envelope)
            return True
        except Exception as exc:
            if self.queue:
                self.queue.enqueue("provenance", envelope.to_payload(), envelope.envelope_id, error=str(exc))
                return False
            raise

    def flush_queue(self) -> int:
        if not self.queue:
            return 0
        return self.queue.drain("provenance", self._handle_queue_item)

    def _handle_queue_item(self, item: QueueItem) -> None:
        payload = dict(item.payload)
        envelope_id = item.idempotency_key
        self._post_payload(payload, envelope_id)

    def _post_with_retry(self, envelope: ProvenanceEnvelope) -> None:
        attempt = 0
        while True:
            try:
                self._post_payload(envelope.to_payload(), envelope.envelope_id)
                return
            except (URLError, HTTPError, RuntimeError) as exc:
                attempt += 1
                if attempt >= self.max_attempts:
                    raise RuntimeError(f"provenance_emit_failed: {exc}") from exc
                time.sleep(self.backoff_seconds * (2 ** (attempt - 1)))

    def _post_payload(self, payload: Dict[str, Any], idempotency_key: str) -> None:
        status, _ = post_json(
            f"{self.base_url}/api/ingest/provenance",
            payload,
            headers={
                "Content-Type": "application/json",
                "Authorization": f"Bearer {self.jwt}",
                "Idempotency-Key": idempotency_key,
            },
            timeout_seconds=self.timeout_seconds,
        )
        if status >= 400:
            raise RuntimeError(f"provenance_emit_failed: {status}")


def build_envelope(
    summary: ProvenanceSummary,
    connector_id: str,
    source: str,
    tenant_id: str | None = None,
    workspace_id: str | None = None,
    run_id: str | None = None,
) -> ProvenanceEnvelope:
    envelope_id = f"{connector_id}:{run_id or utc_now()}"
    return ProvenanceEnvelope(
        version="v1",
        envelope_id=envelope_id,
        emitted_utc=utc_now(),
        connector_id=connector_id,
        source=source,
        summary=summary,
        tenant_id=tenant_id,
        workspace_id=workspace_id,
        run_id=run_id,
    )
