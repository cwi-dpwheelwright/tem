from __future__ import annotations

from typing import Any, Dict, List

from .graph_artifacts import GraphEntityRecord, GraphRelationshipRecord, load_entity_records, load_relationship_records
from .graph_ontology import GraphOntology, load_graph_ontology, reject_candidate


DEFAULT_ENTITY_THRESHOLD = 0.85
DEFAULT_RELATIONSHIP_THRESHOLD = 0.80


def curate_rollup(
    rollup: Dict[str, Any],
    *,
    ontology: GraphOntology | None = None,
    entity_threshold: float = DEFAULT_ENTITY_THRESHOLD,
    relationship_threshold: float = DEFAULT_RELATIONSHIP_THRESHOLD,
) -> Dict[str, Any]:
    ontology = ontology or load_graph_ontology()
    pack_target = rollup.get("pack_target")

    original_entities = load_entity_records(rollup)
    curated_entities: List[GraphEntityRecord] = []
    rejected_entity_count = 0
    for entity in original_entities:
        if entity.confidence < entity_threshold:
            rejected_entity_count += 1
            continue
        if reject_candidate(entity.canonical_name, ontology):
            rejected_entity_count += 1
            continue
        if not ontology.is_supported_entity_type(entity.entity_type):
            rejected_entity_count += 1
            continue
        if entity.pack_target and pack_target and entity.pack_target != pack_target:
            rejected_entity_count += 1
            continue
        curated_entities.append(entity)

    allowed_entity_ids = {entity.entity_id for entity in curated_entities}
    original_relationships = load_relationship_records(rollup)
    curated_relationships: List[GraphRelationshipRecord] = []
    rejected_relationship_count = 0
    for relationship in original_relationships:
        if relationship.confidence < relationship_threshold:
            rejected_relationship_count += 1
            continue
        if not ontology.is_supported_relationship_type(relationship.relationship_type):
            rejected_relationship_count += 1
            continue
        if relationship.source_entity_id not in allowed_entity_ids or relationship.target_entity_id not in allowed_entity_ids:
            rejected_relationship_count += 1
            continue
        if relationship.source_pack_target and pack_target and relationship.source_pack_target != pack_target:
            rejected_relationship_count += 1
            continue
        curated_relationships.append(relationship)

    curation_summary = {
        "original_entity_count": len(original_entities),
        "curated_entity_count": len(curated_entities),
        "original_relationship_count": len(original_relationships),
        "curated_relationship_count": len(curated_relationships),
        "rejected_entity_count": rejected_entity_count,
        "rejected_relationship_count": rejected_relationship_count,
        "allowed_file_count": len(rollup.get("files", []) or []),
        "original_file_count": len(rollup.get("files", []) or []),
    }

    return {
        **rollup,
        "entity_count": len(curated_entities),
        "relationship_count": len(curated_relationships),
        "entities": [entity.to_dict() for entity in curated_entities],
        "relationships": [relationship.to_dict() for relationship in curated_relationships],
        "curation_summary": curation_summary,
    }
