from __future__ import annotations

import time
from dataclasses import dataclass
from typing import Any, Dict
from urllib.error import HTTPError, URLError

from .http_client import post_json
from .normalization import DocumentChunk, DocumentRecord
from .url_validation import validate_http_url


@dataclass(frozen=True)
class VectorIngestResult:
    ok: bool
    status_code: int
    error: str | None


class VectorIngestClient:
    def __init__(
        self,
        base_url: str,
        jwt: str,
        timeout_seconds: float = 20.0,
        max_attempts: int = 3,
        backoff_seconds: float = 1.0,
    ) -> None:
        self.base_url = validate_http_url(base_url, "base_url").rstrip("/")
        self.jwt = jwt
        self.timeout_seconds = timeout_seconds
        self.max_attempts = max_attempts
        self.backoff_seconds = backoff_seconds

    def ingest_document(self, record: DocumentRecord, idempotency_key: str | None = None) -> VectorIngestResult:
        idempotency_key = idempotency_key or record.doc_id
        payload = {
            "version": "v1",
            "tenant_id": record.metadata.get("tenant_id") if record.metadata else "tenant-1",
            "idempotency_key": idempotency_key,
            "document": {
                "doc_id": record.doc_id,
                "title": record.title,
                "mime_type": record.mime_type,
                "content": record.content,
                "content_encoding": "plain",
                "source": record.source,
                "metadata": record.metadata or {},
            },
        }
        return self._post_with_retry("/api/ingest/document", payload, idempotency_key)

    def ingest_chunk(
        self,
        chunk: DocumentChunk,
        parent: DocumentRecord,
        idempotency_key: str | None = None,
    ) -> VectorIngestResult:
        idempotency_key = idempotency_key or chunk.chunk_id
        metadata = dict(parent.metadata or {})
        metadata.update({"parent_doc_id": parent.doc_id, "chunk_index": chunk.index})
        payload = {
            "version": "v1",
            "tenant_id": metadata.get("tenant_id") or "tenant-1",
            "idempotency_key": idempotency_key,
            "document": {
                "doc_id": chunk.chunk_id,
                "title": parent.title,
                "mime_type": parent.mime_type,
                "content": chunk.content,
                "content_encoding": "plain",
                "source": parent.source,
                "metadata": metadata,
            },
        }
        return self._post_with_retry("/api/ingest/document", payload, idempotency_key)

    def delete_document(self, doc_id: str, tenant_id: str, idempotency_key: str | None = None) -> VectorIngestResult:
        idempotency_key = idempotency_key or f"delete:{doc_id}"
        payload = {
            "version": "v1",
            "tenant_id": tenant_id,
            "doc_id": doc_id,
        }
        return self._post_with_retry("/api/ingest/document/delete", payload, idempotency_key)

    def _post_with_retry(self, path: str, payload: Dict[str, Any], idempotency_key: str) -> VectorIngestResult:
        attempt = 0
        while True:
            try:
                return self._post_payload(path, payload, idempotency_key)
            except (URLError, HTTPError, RuntimeError):
                attempt += 1
                if attempt >= self.max_attempts:
                    return VectorIngestResult(ok=False, status_code=503, error="ingest_failed")
                time.sleep(self.backoff_seconds * (2 ** (attempt - 1)))

    def _post_payload(self, path: str, payload: Dict[str, Any], idempotency_key: str) -> VectorIngestResult:
        status, _ = post_json(
            f"{self.base_url}{path}",
            payload,
            headers={
                "Content-Type": "application/json",
                "Authorization": f"Bearer {self.jwt}",
                "Idempotency-Key": idempotency_key,
            },
            timeout_seconds=self.timeout_seconds,
        )
        if status >= 400:
            return VectorIngestResult(ok=False, status_code=status, error="ingest_failed")
        return VectorIngestResult(ok=True, status_code=status, error=None)
