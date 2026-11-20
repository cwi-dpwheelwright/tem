from __future__ import annotations

from typing import Any, Dict, Iterable, List

from .graph_artifacts import dedupe_entities, dedupe_relationships, load_entity_records, load_relationship_records, utc_now


def build_rollup(
    pack_target: str,
    entity_artifacts: Iterable[Dict[str, Any]],
    relationship_artifacts: Iterable[Dict[str, Any]],
    *,
    generated_at: str | None = None,
) -> Dict[str, Any]:
    entity_payloads = list(entity_artifacts)
    relationship_payloads = list(relationship_artifacts)

    entities = dedupe_entities(
        entity
        for payload in entity_payloads
        for entity in load_entity_records(payload)
    )
    relationships = dedupe_relationships(
        relationship
        for payload in relationship_payloads
        for relationship in load_relationship_records(payload)
    )

    files = sorted(
        {
            str(payload.get("file_id") or "")
            for payload in [*entity_payloads, *relationship_payloads]
            if payload.get("file_id")
        }
    )

    return {
        "pack_target": pack_target,
        "generated_at": generated_at or utc_now(),
        "file_count": len(files),
        "entity_count": len(entities),
        "relationship_count": len(relationships),
        "files": files,
        "entities": [entity.to_dict() for entity in entities],
        "relationships": [relationship.to_dict() for relationship in relationships],
    }
