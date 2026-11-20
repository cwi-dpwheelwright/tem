from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from typing import Any, Dict, Iterable, List

from .canonical_record import CanonicalRecord, build_canonical_record
from .canonical_schema import validate_canonical_record
from .document_schema import validate_document
from .normalization import DocumentChunk, DocumentRecord, chunk_document, normalize_document
from .provenance import ProvenanceClient, ProvenanceSummary, build_envelope
from .reconciliation import ReconciliationClient, ReconciliationResult
from .retention import RetentionEnforcer
from .vector_ingest import VectorIngestClient


@dataclass(frozen=True)
class PipelineConfig:
    chunk_size: int
    chunk_overlap: int
    redaction_tokens: List[str]
    retention_days: int | None = None


@dataclass(frozen=True)
class PipelineResult:
    documents: List[DocumentRecord]
    chunks: List[DocumentChunk]
    canonical_records: List[CanonicalRecord]
    reconciliation_results: List[ReconciliationResult]
    skipped: int
    retention_deleted: List[str]


@dataclass(frozen=True)
class PipelineContext:
    tenant_id: str
    workspace_id: str
    connector_id: str
    connector_type: str
    source: str
    source_priority: int
    run_id: str


class ConnectorPipeline:
    def __init__(
        self,
        config: PipelineConfig,
        vector_client: VectorIngestClient | None,
        provenance_client: ProvenanceClient | None,
        reconciliation_client: ReconciliationClient | None = None,
        retention_enforcer: RetentionEnforcer | None = None,
    ) -> None:
        self.config = config
        self.vector_client = vector_client
        self.provenance_client = provenance_client
        self.reconciliation_client = reconciliation_client
        self.retention_enforcer = retention_enforcer

    def run(self, raw_items: Iterable[Dict[str, Any]], context: PipelineContext) -> PipelineResult:
        documents: List[DocumentRecord] = []
        chunks: List[DocumentChunk] = []
        canonical_records: List[CanonicalRecord] = []
        reconciliation_results: List[ReconciliationResult] = []
        skipped = 0
        seen_ids: set[str] = set()
        retention_deleted: List[str] = []

        for item in raw_items:
            try:
                record = normalize_document(
                    item,
                    redaction_tokens=self.config.redaction_tokens,
                    extra_metadata={
                        "tenant_id": context.tenant_id,
                        "workspace_id": context.workspace_id,
                        "connector_id": context.connector_id,
                        "connector_type": context.connector_type,
                        "source_priority": context.source_priority,
                        "run_id": context.run_id,
                    },
                    source_override=context.source,
                )
            except ValueError:
                skipped += 1
                continue
            try:
                validate_document(record.__dict__)
            except ValueError:
                skipped += 1
                continue

            if record.doc_id in seen_ids:
                skipped += 1
                continue
            seen_ids.add(record.doc_id)

            if self._is_expired(record):
                skipped += 1
                continue

            documents.append(record)
            chunks.extend(chunk_document(record, self.config.chunk_size, self.config.chunk_overlap))
            canonical_record = build_canonical_record(
                record,
                context=context,
                connector_type=context.connector_type,
                source_priority=context.source_priority,
                run_id=context.run_id,
            )
            try:
                validate_canonical_record(canonical_record.to_payload())
            except ValueError:
                skipped += 1
                continue
            canonical_records.append(canonical_record)

        if self.vector_client:
            for doc in documents:
                self.vector_client.ingest_document(doc, idempotency_key=doc.doc_id)
            for chunk in chunks:
                parent = next((doc for doc in documents if doc.doc_id == chunk.doc_id), None)
                if parent:
                    self.vector_client.ingest_chunk(chunk, parent, idempotency_key=chunk.chunk_id)

        if self.provenance_client:
            summary = ProvenanceSummary(
                source=context.source,
                doc_ids=[doc.doc_id for doc in documents],
                chunk_ids=[chunk.chunk_id for chunk in chunks],
                redaction_count=len(self.config.redaction_tokens),
            )
            envelope = build_envelope(
                summary,
                connector_id=context.connector_id,
                source=context.source,
                tenant_id=context.tenant_id,
                workspace_id=context.workspace_id,
                run_id=context.run_id,
            )
            self.provenance_client.emit(envelope)

        if self.reconciliation_client and canonical_records:
            reconciliation_results = self.reconciliation_client.submit_batch(canonical_records)

        if self.retention_enforcer and documents:
            self.retention_enforcer.record(documents, tenant_id=context.tenant_id)
            retention_deleted = self.retention_enforcer.purge_expired()

        return PipelineResult(
            documents=documents,
            chunks=chunks,
            canonical_records=canonical_records,
            reconciliation_results=reconciliation_results,
            skipped=skipped,
            retention_deleted=retention_deleted,
        )

    def _is_expired(self, record: DocumentRecord) -> bool:
        if not self.config.retention_days:
            return False
        metadata = record.metadata or {}
        created_raw = metadata.get("created_utc") or metadata.get("created_at")
        if not created_raw:
            return False
        try:
            created = datetime.fromisoformat(str(created_raw).replace("Z", "+00:00"))
        except ValueError:
            return False
        if created.tzinfo is None:
            created = created.replace(tzinfo=timezone.utc)
        return created < datetime.now(timezone.utc) - timedelta(days=self.config.retention_days)
