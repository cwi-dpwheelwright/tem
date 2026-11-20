from __future__ import annotations

import json
import time
import uuid
from dataclasses import dataclass
from typing import Any, Dict
from urllib.request import Request, urlopen
from urllib.error import URLError, HTTPError

from cwi_etl.url_validation import validate_http_url

@dataclass(frozen=True)
class ProvenanceEvent:
    tenant_id: str
    run_id: str
    doc_id: str
    chunk_count: int
    idempotency_key: str
    started_utc: str
    completed_utc: str
    status: str
    metadata: Dict[str, Any]

    def to_payload(self) -> Dict[str, Any]:
        return {
            "tenant_id": self.tenant_id,
            "run_id": self.run_id,
            "doc_id": self.doc_id,
            "chunk_count": self.chunk_count,
            "idempotency_key": self.idempotency_key,
            "started_utc": self.started_utc,
            "completed_utc": self.completed_utc,
            "status": self.status,
            "metadata": self.metadata,
        }


class ProvenanceClient:
    def __init__(self, base_url: str, jwt: str, timeout_seconds: float = 10.0) -> None:
        self.base_url = validate_http_url(base_url, "base_url").rstrip("/")
        self.jwt = jwt
        self.timeout_seconds = timeout_seconds

    def send(self, event: ProvenanceEvent) -> None:
        body = json.dumps(event.to_payload()).encode("utf-8")
        request = Request(
            f"{self.base_url}/api/ingest/provenance",
            data=body,
            headers={
                "Content-Type": "application/json",
                "Authorization": f"Bearer {self.jwt}",
                "Idempotency-Key": event.idempotency_key,
            },
            method="POST",
        )
        with urlopen(request, timeout=self.timeout_seconds) as response:  # nosemgrep: python.lang.security.audit.dynamic-urllib-use-detected
            if response.status >= 400:
                raise RuntimeError(f"Provenance ingest failed with status {response.status}")


def post_with_retry(
    client: ProvenanceClient,
    event: ProvenanceEvent,
    max_attempts: int = 3,
    base_delay: float = 1.0,
) -> None:
    attempt = 0
    while True:
        try:
            client.send(event)
            return
        except (URLError, HTTPError, RuntimeError) as exc:
            attempt += 1
            if attempt >= max_attempts:
                raise RuntimeError(f"Provenance ingest failed after {attempt} attempts: {exc}") from exc
            delay = base_delay * (2 ** (attempt - 1))
            time.sleep(delay)


def build_provenance_event(
    tenant_id: str,
    doc_id: str,
    chunk_count: int,
    idempotency_key: str,
    started_utc: str,
    completed_utc: str,
    status: str = "completed",
    metadata: Dict[str, Any] | None = None,
) -> ProvenanceEvent:
    return ProvenanceEvent(
        tenant_id=tenant_id,
        run_id=str(uuid.uuid4()),
        doc_id=doc_id,
        chunk_count=chunk_count,
        idempotency_key=idempotency_key,
        started_utc=started_utc,
        completed_utc=completed_utc,
        status=status,
        metadata=metadata or {},
    )
