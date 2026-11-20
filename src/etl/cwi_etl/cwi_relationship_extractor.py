from __future__ import annotations

from typing import Any, Dict, Iterable, List

from .graph_artifacts import (
    DocumentRelationshipArtifact,
    GraphEntityRecord,
    GraphRelationshipRecord,
    load_entity_records,
    stable_relationship_id,
    utc_now,
)
from .graph_ontology import GraphOntology, infer_document_class, load_graph_ontology


def extract_relationships(
    text: str,
    entity_artifact: Dict[str, Any],
    *,
    ontology: GraphOntology | None = None,
    source_document: str,
    file_id: str | None = None,
    timestamp: str | None = None,
    pack_target: str | None = None,
    metadata: Dict[str, Any] | None = None,
) -> Dict[str, Any]:
    ontology = ontology or load_graph_ontology()
    metadata = metadata or {}
    entities = load_entity_records(entity_artifact)
    resolved_file_id = file_id or str(entity_artifact.get("file_id") or metadata.get("file_id"))
    resolved_timestamp = timestamp or str(metadata.get("timestamp") or entity_artifact.get("generated_at") or utc_now())
    document_class = str(entity_artifact.get("document_class") or infer_document_class(source_document, ontology, metadata=metadata))
    document_authority = int(entity_artifact.get("document_authority") or metadata.get("document_authority") or 60)
    relationships: Dict[str, GraphRelationshipRecord] = {}

    procedure_entities = [entity for entity in entities if entity.entity_type == "procedure"]
    metrics_by_name = {entity.canonical_name: entity for entity in entities if entity.entity_type == "metric"}

    for line in _iter_lines(text):
        line_matches = _entities_in_line(line, entities)
        equipment_like = [entity for entity in line_matches if entity.entity_type in {"equipment", "component"}]
        metrics = [entity for entity in line_matches if entity.entity_type == "metric"]

        for source in equipment_like:
            for target in metrics:
                record = GraphRelationshipRecord(
                    relationship_id=stable_relationship_id(source.entity_id, "measured_by", target.entity_id),
                    source_entity_id=source.entity_id,
                    target_entity_id=target.entity_id,
                    relationship_type="measured_by",
                    evidence_text=line,
                    confidence=0.93,
                    file_id=resolved_file_id,
                    source_document=source_document,
                    timestamp=resolved_timestamp,
                    document_class=document_class,
                    document_authority=document_authority,
                    source_pack_target=pack_target or source.pack_target,
                    target_pack_target=pack_target or target.pack_target,
                )
                relationships.setdefault(record.relationship_id, record)

        if procedure_entities and metrics:
            procedure = procedure_entities[0]
            for metric in metrics:
                record = GraphRelationshipRecord(
                    relationship_id=stable_relationship_id(procedure.entity_id, "tracks", metric.entity_id),
                    source_entity_id=procedure.entity_id,
                    target_entity_id=metric.entity_id,
                    relationship_type="tracks",
                    evidence_text=line,
                    confidence=0.90,
                    file_id=resolved_file_id,
                    source_document=source_document,
                    timestamp=resolved_timestamp,
                    document_class=document_class,
                    document_authority=document_authority,
                    source_pack_target=pack_target or procedure.pack_target,
                    target_pack_target=pack_target or metric.pack_target,
                )
                relationships.setdefault(record.relationship_id, record)

    artifact = DocumentRelationshipArtifact(
        file_id=resolved_file_id,
        source_document=source_document,
        generated_at=utc_now(),
        document_class=document_class,
        document_authority=document_authority,
        relationship_count=len(relationships),
        relationships=list(relationships.values()),
    )
    return artifact.to_dict()


def _iter_lines(text: str) -> Iterable[str]:
    for raw in text.splitlines():
        line = raw.strip()
        if line:
            yield line


def _entities_in_line(line: str, entities: List[GraphEntityRecord]) -> List[GraphEntityRecord]:
    lowered = line.lower()
    matches = []
    for entity in entities:
        candidates = [entity.canonical_name.replace("_", " "), entity.source_text, *entity.aliases]
        if any(candidate and candidate.lower() in lowered for candidate in candidates):
            matches.append(entity)
    return matches
