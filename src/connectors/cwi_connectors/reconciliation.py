from __future__ import annotations

import json
import time
from dataclasses import dataclass
from typing import Any, Dict, List
from urllib.error import HTTPError, URLError

from .canonical_record import CanonicalRecord
from .delivery_queue import DeliveryQueue, QueueItem
from .http_client import post_json
from .url_validation import validate_http_url


@dataclass(frozen=True)
class ReconciliationResult:
    ok: bool
    status_code: int
    ticket_required: bool
    ticket_id: str | None
    error: str | None = None


class ReconciliationClient:
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

    def submit_batch(self, records: List[CanonicalRecord]) -> List[ReconciliationResult]:
        results = []
        for record in records:
            results.append(self.submit(record))
        return results

    def submit(self, record: CanonicalRecord) -> ReconciliationResult:
        self.flush_queue()
        payload = record.to_payload()
        idempotency_key = f"reconcile:{record.record_id}"
        try:
            response = self._post_with_retry(payload, idempotency_key)
            ticket_id = None
            ticket_required = False
            if isinstance(response, dict):
                ticket = response.get("approval_ticket") or {}
                ticket_id = ticket.get("ticket_id") if isinstance(ticket, dict) else None
                ticket_required = bool(ticket_id)
            return ReconciliationResult(
                ok=True,
                status_code=200,
                ticket_required=ticket_required,
                ticket_id=ticket_id,
            )
        except Exception as exc:
            if self.queue:
                self.queue.enqueue("reconciliation", payload, idempotency_key, error=str(exc))
            return ReconciliationResult(
                ok=False,
                status_code=503,
                ticket_required=False,
                ticket_id=None,
                error=str(exc),
            )

    def flush_queue(self) -> int:
        if not self.queue:
            return 0
        return self.queue.drain("reconciliation", self._handle_queue_item)

    def _handle_queue_item(self, item: QueueItem) -> None:
        payload = dict(item.payload)
        self._post_with_retry(payload, item.idempotency_key)

    def _post_with_retry(self, payload: Dict[str, Any], idempotency_key: str) -> Dict[str, Any] | None:
        attempt = 0
        while True:
            try:
                return self._post_payload(payload, idempotency_key)
            except (URLError, HTTPError, RuntimeError) as exc:
                attempt += 1
                if attempt >= self.max_attempts:
                    raise RuntimeError(f"reconciliation_submit_failed: {exc}") from exc
                time.sleep(self.backoff_seconds * (2 ** (attempt - 1)))

    def _post_payload(self, payload: Dict[str, Any], idempotency_key: str) -> Dict[str, Any] | None:
        status, body = post_json(
            f"{self.base_url}/api/reconciliation/records",
            payload,
            headers={
                "Content-Type": "application/json",
                "Authorization": f"Bearer {self.jwt}",
                "Idempotency-Key": idempotency_key,
            },
            timeout_seconds=self.timeout_seconds,
        )
        if status >= 400:
            raise RuntimeError(f"reconciliation_submit_failed: {status}")
        if not body:
            return None
        return json.loads(body)
