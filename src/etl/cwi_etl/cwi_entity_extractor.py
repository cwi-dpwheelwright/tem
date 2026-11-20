from __future__ import annotations

from typing import Any, Dict, Iterable, List

from .graph_artifacts import DocumentEntityArtifact, GraphEntityRecord, stable_file_id, utc_now
from .graph_ontology import (
    GraphOntology,
    build_entity_id,
    document_authority_for_class,
    entity_mentions_in_text,
    infer_document_class,
    load_graph_ontology,
    reject_candidate,
)


def extract_entities(
    text: str,
    *,
    source_document: str,
    ontology: GraphOntology | None = None,
    file_id: str | None = None,
    timestamp: str | None = None,
    pack_target: str | None = None,
    metadata: Dict[str, Any] | None = None,
) -> Dict[str, Any]:
    ontology = ontology or load_graph_ontology()
    metadata = metadata or {}
    resolved_file_id = file_id or str(metadata.get("file_id") or stable_file_id(source_document))
    resolved_timestamp = timestamp or str(metadata.get("timestamp") or utc_now())
    document_class = infer_document_class(source_document, ontology, metadata=metadata)
    document_authority = document_authority_for_class(document_class, ontology, metadata=metadata)

    entities: Dict[str, GraphEntityRecord] = {}
    for line in _iter_lines(text):
        for definition, matched_text in entity_mentions_in_text(line, ontology):
            if reject_candidate(definition.canonical_name, ontology):
                continue
            entity_id = build_entity_id(definition.entity_type, definition.canonical_name)
            record = GraphEntityRecord(
                entity_id=entity_id,
                entity_type=definition.entity_type,
                canonical_name=definition.canonical_name,
                source_text=matched_text,
                confidence=_confidence_for_match(line, matched_text),
                file_id=resolved_file_id,
                source_document=source_document,
                timestamp=resolved_timestamp,
                document_class=document_class,
                document_authority=document_authority,
                pack_target=pack_target or _safe_string(metadata.get("pack_target")),
                domain=definition.domain,
                aliases=list(definition.aliases),
            )
            existing = entities.get(entity_id)
            if existing is None or record.confidence > existing.confidence:
                entities[entity_id] = record

    procedure = _infer_procedure_entity(
        source_document=source_document,
        ontology=ontology,
        file_id=resolved_file_id,
        timestamp=resolved_timestamp,
        document_class=document_class,
        document_authority=document_authority,
        pack_target=pack_target or _safe_string(metadata.get("pack_target")),
    )
    if procedure is not None:
        entities.setdefault(procedure.entity_id, procedure)

    artifact = DocumentEntityArtifact(
        file_id=resolved_file_id,
        source_document=source_document,
        generated_at=utc_now(),
        document_class=document_class,
        document_authority=document_authority,
        entity_count=len(entities),
        entities=list(entities.values()),
    )
    return artifact.to_dict()


def _iter_lines(text: str) -> Iterable[str]:
    for raw in text.splitlines():
        line = raw.strip()
        if line:
            yield line


def _confidence_for_match(line: str, matched_text: str) -> float:
    if line.strip().lower() == matched_text.strip().lower():
        return 0.95
    return 0.90


def _infer_procedure_entity(
    *,
    source_document: str,
    ontology: GraphOntology,
    file_id: str,
    timestamp: str,
    document_class: str,
    document_authority: int,
    pack_target: str | None,
) -> GraphEntityRecord | None:
    matches = entity_mentions_in_text(source_document, ontology, allowed_types=["procedure"])
    if not matches:
        return None
    definition, matched_text = matches[0]
    return GraphEntityRecord(
        entity_id=build_entity_id(definition.entity_type, definition.canonical_name),
        entity_type=definition.entity_type,
        canonical_name=definition.canonical_name,
        source_text=matched_text,
        confidence=0.88,
        file_id=file_id,
        source_document=source_document,
        timestamp=timestamp,
        document_class=document_class,
        document_authority=document_authority,
        pack_target=pack_target,
        domain=definition.domain,
        aliases=list(definition.aliases),
        metadata={"inferred_from": "source_document"},
    )


def _safe_string(value: Any) -> str | None:
    if value is None:
        return None
    text = str(value).strip()
    return text or None
