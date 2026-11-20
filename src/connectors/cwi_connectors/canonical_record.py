from __future__ import annotations

import hashlib
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any, Dict, List

from .framework import ConnectorContext
from .normalization import DocumentRecord
from .state_store import utc_now


@dataclass(frozen=True)
class SourceRecord:
    source_id: str
    updated_utc: str
    attributes: Dict[str, Any]


@dataclass(frozen=True)
class CanonicalRecord:
    record_id: str
    tenant_id: str
    user_id: str
    canonical: Dict[str, Any]
    sources: List[SourceRecord]

    def to_payload(self) -> Dict[str, Any]:
        return {
            "record_id": self.record_id,
            "tenant_id": self.tenant_id,
            "user_id": self.user_id,
            "canonical": self.canonical,
            "sources": [source.__dict__ for source in self.sources],
        }


def build_canonical_record(
    record: DocumentRecord,
    context: ConnectorContext,
    connector_type: str,
    source_priority: int,
    run_id: str,
    user_id: str | None = None,
) -> CanonicalRecord:
    record_id = _resolve_record_id(record)
    updated_utc = _resolve_updated_utc(record.metadata or {})
    source_id = _resolve_source_id(record, context)
    provenance = {
        "connector_id": context.connector_id,
        "connector_type": connector_type,
        "run_id": run_id,
        "workspace_id": context.workspace_id,
    }
    if record.metadata and record.metadata.get("cursor"):
        provenance["cursor"] = record.metadata.get("cursor")
    attributes = dict(record.metadata or {})
    attributes.update(
        {
            "priority": source_priority,
            "source": record.source,
            "document_id": record.doc_id,
            "provenance": provenance,
        }
    )
    sources = [SourceRecord(source_id=source_id, updated_utc=updated_utc, attributes=attributes)]
    canonical = {
        "doc_id": record.doc_id,
        "title": record.title,
        "mime_type": record.mime_type,
        "source": record.source,
        "content_hash": _hash_content(record.content),
        "content_bytes": len(record.content),
        "metadata": record.metadata or {},
    }
    return CanonicalRecord(
        record_id=record_id,
        tenant_id=context.tenant_id,
        user_id=user_id or context.connector_id,
        canonical=canonical,
        sources=sources,
    )


def _resolve_record_id(record: DocumentRecord) -> str:
    metadata = record.metadata or {}
    return str(metadata.get("record_id") or metadata.get("entity_id") or record.doc_id)


def _resolve_source_id(record: DocumentRecord, context: ConnectorContext) -> str:
    metadata = record.metadata or {}
    return str(metadata.get("source_id") or f"{context.connector_id}:{record.doc_id}")


def _resolve_updated_utc(metadata: Dict[str, Any]) -> str:
    value = metadata.get("updated_utc") or metadata.get("updated_at") or metadata.get("created_utc")
    if isinstance(value, datetime):
        if value.tzinfo is None:
            value = value.replace(tzinfo=timezone.utc)
        return value.isoformat().replace("+00:00", "Z")
    if isinstance(value, str) and value:
        return value
    return utc_now()


def _hash_content(content: str) -> str:
    if not content:
        return ""
    return hashlib.sha256(content.encode("utf-8")).hexdigest()
