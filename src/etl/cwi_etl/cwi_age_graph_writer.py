from __future__ import annotations

from typing import Any, Dict, Iterable, List, Tuple

from .cwi_graph_crosswalk import build_crosswalk
from .graph_artifacts import GraphRelationshipRecord, load_entity_records, load_relationship_records, utc_now
from .graph_rag import GraphProjection
from .graph_store import DEFAULT_GRAPH_NAME, GraphIngestClient, GraphStoreConfig, build_edge_cypher, build_node_cypher


def build_graph_projection_from_artifacts(
    curated_rollups: Iterable[Dict[str, Any]],
    crosswalk: Dict[str, Any] | None = None,
    *,
    tenant_id: str = "global",
    graph_id: str = DEFAULT_GRAPH_NAME,
    source: str = "teacher",
    generated_at: str | None = None,
) -> GraphProjection:
    timestamp = generated_at or utc_now()
    nodes_by_id: Dict[str, Dict[str, object]] = {}
    edges_by_id: Dict[str, Dict[str, object]] = {}

    for rollup in curated_rollups:
        pack_target = str(rollup.get("pack_target") or "")
        for entity in load_entity_records(rollup):
            nodes_by_id.setdefault(
                entity.entity_id,
                {
                    "node_id": entity.entity_id,
                    "labels": [entity.entity_type.capitalize(), "GraphEntity"],
                    "tenant_id": tenant_id,
                    "graph_id": graph_id,
                    "source": source,
                    "created_utc": timestamp,
                    "updated_utc": timestamp,
                    "properties": {
                        "entity_id": entity.entity_id,
                        "entity_type": entity.entity_type,
                        "canonical_name": entity.canonical_name,
                        "source_document": entity.source_document,
                        "document_class": entity.document_class,
                        "document_authority": entity.document_authority,
                        "pack_target": entity.pack_target or pack_target,
                    },
                },
            )
        for relationship in load_relationship_records(rollup):
            edges_by_id.setdefault(
                relationship.relationship_id,
                _edge_payload(relationship, tenant_id=tenant_id, graph_id=graph_id, source=source, timestamp=timestamp),
            )

    for relationship in load_relationship_records(crosswalk or {}):
        edges_by_id.setdefault(
            relationship.relationship_id,
            _edge_payload(relationship, tenant_id=tenant_id, graph_id=graph_id, source=source, timestamp=timestamp),
        )

    return GraphProjection(
        nodes=sorted(nodes_by_id.values(), key=lambda item: str(item["node_id"])),
        edges=sorted(edges_by_id.values(), key=lambda item: str(item["edge_id"])),
    )


def build_age_artifacts(
    curated_rollups: Iterable[Dict[str, Any]],
    crosswalk: Dict[str, Any] | None = None,
    *,
    graph_name: str = DEFAULT_GRAPH_NAME,
    tenant_id: str = "global",
    source: str = "teacher",
    generated_at: str | None = None,
) -> Tuple[str, Dict[str, Any]]:
    timestamp = generated_at or utc_now()
    rollups = list(curated_rollups)
    projection = build_graph_projection_from_artifacts(
        rollups,
        crosswalk,
        tenant_id=tenant_id,
        graph_id=graph_name,
        source=source,
        generated_at=timestamp,
    )

    sql_lines = [
        "CREATE EXTENSION IF NOT EXISTS age;",
        "LOAD 'age';",
        "SET search_path = ag_catalog, \"$user\", public;",
        "DO $$ BEGIN",
        f"  IF NOT EXISTS (SELECT 1 FROM ag_catalog.ag_graph WHERE name = '{graph_name}') THEN",
        f"    PERFORM ag_catalog.create_graph('{graph_name}');",
        "  END IF;",
        "END $$;",
        "",
    ]

    for node in projection.nodes:
        query = build_node_cypher(node)
        sql_lines.append(_cypher_statement(graph_name, query))
    for edge in projection.edges:
        query = build_edge_cypher(edge)
        sql_lines.append(_cypher_statement(graph_name, query))

    crosswalk_count = len((crosswalk or {}).get("relationships", []) or [])
    summary = {
        "generated_at": timestamp,
        "graph_name": graph_name,
        "rollup_file_count": len(rollups),
        "vertex_count": len(projection.nodes),
        "edge_count": len(projection.edges),
        "crosspack_relationship_count": crosswalk_count,
        "crosspack_loaded": crosswalk_count > 0,
        "rollups": [str(rollup.get("pack_target") or "") for rollup in rollups],
    }
    return "\n".join(sql_lines).rstrip() + "\n", summary


def load_age_projection(
    curated_rollups: Iterable[Dict[str, Any]],
    *,
    config: GraphStoreConfig,
    crosswalk: Dict[str, Any] | None = None,
    tenant_id: str = "global",
    source: str = "teacher",
    generated_at: str | None = None,
) -> None:
    projection = build_graph_projection_from_artifacts(
        curated_rollups,
        crosswalk,
        tenant_id=tenant_id,
        graph_id=config.graph_name,
        source=source,
        generated_at=generated_at,
    )
    GraphIngestClient(config).ingest_projection(projection)


def _cypher_statement(graph_name: str, query: str) -> str:
    quoted = _dollar_quote(query, "graph_artifact")
    return f"SELECT * FROM ag_catalog.cypher('{graph_name}', {quoted}) as (result agtype);"


def _dollar_quote(value: str, tag: str) -> str:
    token = f"cwi_{tag}"
    while f"${token}$" in value:
        token += "x"
    return f"${token}$" + value + f"${token}$"


def _edge_payload(
    relationship: GraphRelationshipRecord,
    *,
    tenant_id: str,
    graph_id: str,
    source: str,
    timestamp: str,
) -> Dict[str, object]:
    return {
        "edge_id": relationship.relationship_id,
        "from_node_id": relationship.source_entity_id,
        "to_node_id": relationship.target_entity_id,
        "relation": relationship.relationship_type.upper(),
        "tenant_id": tenant_id,
        "graph_id": graph_id,
        "source": source,
        "created_utc": timestamp,
        "updated_utc": timestamp,
        "properties": {
            "relationship_type": relationship.relationship_type,
            "relationship_mode": relationship.relationship_mode,
            "confidence": relationship.confidence,
            "source_document": relationship.source_document,
            "document_class": relationship.document_class,
            "document_authority": relationship.document_authority,
            "evidence_text": relationship.evidence_text,
            "source_pack_target": relationship.source_pack_target,
            "target_pack_target": relationship.target_pack_target,
        },
    }
