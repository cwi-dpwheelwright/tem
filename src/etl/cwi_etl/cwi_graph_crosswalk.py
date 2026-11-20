from __future__ import annotations

from typing import Any, Dict, Iterable, List, Tuple

from .graph_artifacts import GraphEntityRecord, GraphRelationshipRecord, load_entity_records, stable_relationship_id, utc_now
from .graph_ontology import GraphOntology, infer_pack_role, load_graph_ontology


def build_crosswalk(
    curated_rollups: Iterable[Dict[str, Any]],
    *,
    ontology: GraphOntology | None = None,
    generated_at: str | None = None,
) -> Dict[str, Any]:
    ontology = ontology or load_graph_ontology()
    rollups = list(curated_rollups)
    entity_refs: Dict[str, List[Tuple[str, str | None, GraphEntityRecord]]] = {}

    for rollup in rollups:
        pack_target = str(rollup.get("pack_target") or "")
        pack_role = infer_pack_role(pack_target, ontology)
        for entity in load_entity_records(rollup):
            entity_refs.setdefault(entity.entity_id, []).append((pack_target, pack_role, entity))

    relationships: Dict[str, GraphRelationshipRecord] = {}
    for entity_id, entries in entity_refs.items():
        manual_entries = [entry for entry in entries if entry[1] == "manual_reference"]
        operation_entries = [entry for entry in entries if entry[1] == "operations"]
        maintenance_entries = [entry for entry in entries if entry[1] == "maintenance"]

        for manual_pack, _, source_entity in manual_entries:
            for target_pack, _, target_entity in operation_entries:
                if source_entity.entity_type not in {"equipment", "component", "metric", "line", "procedure"}:
                    continue
                _register_crosspack(
                    relationships,
                    relationship_type="monitored_in",
                    source_entity=source_entity,
                    target_entity=target_entity,
                    source_pack_target=manual_pack,
                    target_pack_target=target_pack,
                    evidence_text=f"Reference/manual entity appears in operations context: {source_entity.canonical_name}",
                )
            for target_pack, _, target_entity in maintenance_entries:
                if source_entity.entity_type not in {"equipment", "component", "metric", "procedure"}:
                    continue
                _register_crosspack(
                    relationships,
                    relationship_type="maintained_in",
                    source_entity=source_entity,
                    target_entity=target_entity,
                    source_pack_target=manual_pack,
                    target_pack_target=target_pack,
                    evidence_text=f"Reference/manual entity appears in maintenance context: {source_entity.canonical_name}",
                )

        procedure_entries = [entry for entry in entries if entry[2].entity_type == "procedure"]
        for index, (source_pack, _, source_entity) in enumerate(procedure_entries):
            for target_pack, _, target_entity in procedure_entries[index + 1 :]:
                if source_pack == target_pack:
                    continue
                _register_crosspack(
                    relationships,
                    relationship_type="procedure_family_link",
                    source_entity=source_entity,
                    target_entity=target_entity,
                    source_pack_target=source_pack,
                    target_pack_target=target_pack,
                    evidence_text=f"Procedure family inferred across pack contexts: {source_entity.canonical_name}",
                )

    items = sorted(relationships.values(), key=lambda item: (item.relationship_type, item.source_entity_id, item.target_entity_id))
    return {
        "generated_at": generated_at or utc_now(),
        "allowed_packs": sorted({str(rollup.get("pack_target") or "") for rollup in rollups if rollup.get("pack_target")}),
        "crosspack_relationship_count": len(items),
        "relationships": [item.to_dict() for item in items],
    }


def _register_crosspack(
    relationships: Dict[str, GraphRelationshipRecord],
    *,
    relationship_type: str,
    source_entity: GraphEntityRecord,
    target_entity: GraphEntityRecord,
    source_pack_target: str,
    target_pack_target: str,
    evidence_text: str,
) -> None:
    relationship_id = stable_relationship_id(
        source_entity.entity_id,
        relationship_type,
        target_entity.entity_id,
        source_pack_target,
        target_pack_target,
    )
    relationships.setdefault(
        relationship_id,
        GraphRelationshipRecord(
            relationship_id=relationship_id,
            source_entity_id=source_entity.entity_id,
            target_entity_id=target_entity.entity_id,
            relationship_type=relationship_type,
            evidence_text=evidence_text,
            confidence=0.96 if relationship_type == "monitored_in" else 0.94,
            file_id="crosspack",
            source_document=f"{source_entity.source_document} | {target_entity.source_document}",
            timestamp=utc_now(),
            document_class="crosspack_inference",
            document_authority=95,
            relationship_mode="inferred",
            source_pack_target=source_pack_target,
            target_pack_target=target_pack_target,
            metadata={
                "source_document": source_entity.source_document,
                "target_document": target_entity.source_document,
            },
        ),
    )
