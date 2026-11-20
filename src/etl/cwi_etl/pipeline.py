from __future__ import annotations

import os
import uuid
from typing import Iterable, List

from cwi_etl.identity import build_content_identity
from cwi_etl.ir import IRDocument
from cwi_etl.cwi_entity_extractor import extract_entities
from cwi_etl.cwi_relationship_extractor import extract_relationships
from cwi_etl.cwi_graph_rollup import build_rollup
from cwi_etl.cwi_graph_curate import curate_rollup
from cwi_etl.cwi_graph_crosswalk import build_crosswalk
from cwi_etl.cwi_age_graph_writer import build_age_artifacts
from cwi_etl.graph_rag import GraphExtractionHooks, GraphProjection, build_graph_projection
from cwi_etl.graph_store import GraphIngestClient, GraphStoreConfig
from cwi_etl.consent_store import ConsentStore, ConsentStoreConfig
from cwi_etl.consent_remote import RemoteConsentClient, RemoteConsentConfig
from cwi_etl.operator_memory import (
    CorrelationConfig,
    ConsentMissingError,
    ConsentRecord,
    OperatorMemoryCorrelation,
    OperatorMemorySignalBatch,
    build_operator_memory_batch,
    build_operator_memory_correlation,
)
from cwi_etl.reconciliation import ConflictResolutionPolicy, ReconciliationResult, reconcile_documents
from cwi_etl.report import ReconciliationReport, build_report


def deduplicate_documents(documents: Iterable[IRDocument]) -> List[IRDocument]:
    seen = set()
    unique: List[IRDocument] = []
    for document in documents:
        identity = build_content_identity(document.source_id, str(document.to_dict()), doc_id=document.doc_id)
        if identity.stable_id in seen:
            continue
        seen.add(identity.stable_id)
        unique.append(document)
    return unique


def reconcile_documents_to_report(
    documents: Iterable[IRDocument],
    policy: ConflictResolutionPolicy | None = None,
    run_id: str | None = None,
) -> ReconciliationReport:
    result = reconcile_documents(documents, policy)
    return build_report(result, run_id or str(uuid.uuid4()))


def reconcile_documents_with_dedup(
    documents: Iterable[IRDocument],
    policy: ConflictResolutionPolicy | None = None,
) -> ReconciliationResult:
    unique_documents = deduplicate_documents(documents)
    return reconcile_documents(unique_documents, policy)


def project_documents_to_graph(
    documents: Iterable[IRDocument],
    tenant_id: str,
    graph_id: str,
    source: str,
    observed_utc: str,
    hooks: GraphExtractionHooks | None = None,
) -> GraphProjection:
    return build_graph_projection(documents, tenant_id, graph_id, source, observed_utc, hooks=hooks)


def persist_graph_projection(projection: GraphProjection, config: GraphStoreConfig) -> None:
    client = GraphIngestClient(config)
    client.ingest_projection(projection)


def extract_document_graph_artifacts(
    text: str,
    *,
    source_document: str,
    pack_target: str | None = None,
    metadata: dict | None = None,
) -> tuple[dict, dict]:
    entity_artifact = extract_entities(
        text,
        source_document=source_document,
        pack_target=pack_target,
        metadata=metadata,
    )
    relationship_artifact = extract_relationships(
        text,
        entity_artifact,
        source_document=source_document,
        pack_target=pack_target,
        metadata=metadata,
    )
    return entity_artifact, relationship_artifact


def build_pack_graph_rollup(
    pack_target: str,
    entity_artifacts: Iterable[dict],
    relationship_artifacts: Iterable[dict],
) -> dict:
    return build_rollup(pack_target, entity_artifacts, relationship_artifacts)


def curate_pack_graph_rollup(rollup: dict) -> dict:
    return curate_rollup(rollup)


def build_crosspack_graph_edges(curated_rollups: Iterable[dict]) -> dict:
    return build_crosswalk(curated_rollups)


def build_age_load_artifacts(curated_rollups: Iterable[dict], crosswalk: dict | None = None, *, graph_name: str) -> tuple[str, dict]:
    return build_age_artifacts(curated_rollups, crosswalk, graph_name=graph_name)


def ingest_operator_memory_signals(
    tenant_id: str,
    operator_id: str,
    observed_utc: str,
    signals,
    performance,
    *,
    consent: ConsentRecord | None = None,
    session_id: str | None = None,
    source_id: str = "operator-memory",
    enforce_consent: bool = True,
    strict: bool = True,
    consent_store: ConsentStore | None = None,
    consent_store_config: ConsentStoreConfig | None = None,
    consent_remote_config: RemoteConsentConfig | None = None,
) -> OperatorMemorySignalBatch:
    resolved_consent = consent
    if resolved_consent is None and enforce_consent:
        resolver = consent_store
        if resolver is None:
            remote_base = os.environ.get("CWI_CONSENT_REMOTE_BASE_URL", "").strip()
            if consent_remote_config is not None or remote_base:
                remote_config = consent_remote_config or RemoteConsentConfig.from_env()
                resolver = RemoteConsentClient(remote_config)
            else:
                config = consent_store_config or ConsentStoreConfig.from_env()
                resolver = ConsentStore(config)
        resolved_consent = resolver.get_consent_or_raise(
            tenant_id=tenant_id,
            operator_id=operator_id,
            observed_utc=observed_utc,
        )
    if resolved_consent is None and enforce_consent:
        raise ConsentMissingError("consent is required for operator memory ingestion")
    return build_operator_memory_batch(
        tenant_id=tenant_id,
        operator_id=operator_id,
        observed_utc=observed_utc,
        consent=resolved_consent,
        signals=signals,
        performance=performance,
        session_id=session_id,
        source_id=source_id,
        enforce_consent=enforce_consent,
        strict=strict,
    )


def correlate_operator_memory(
    batch: OperatorMemorySignalBatch,
    *,
    correlation_id: str | None = None,
    config: CorrelationConfig | None = None,
    notes: str | None = None,
    metadata: dict | None = None,
) -> OperatorMemoryCorrelation:
    return build_operator_memory_correlation(
        batch,
        correlation_id=correlation_id,
        config=config,
        notes=notes,
        metadata=metadata,
    )
