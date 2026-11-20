from __future__ import annotations

import hashlib
import json
import os
from dataclasses import dataclass
from typing import Callable, Dict, Iterable, List, Tuple

from .ir import IRAttribute, IRDocument, IREntity, IRProvenanceLink


@dataclass(frozen=True)
class GraphProjection:
    nodes: List[Dict[str, object]]
    edges: List[Dict[str, object]]

@dataclass(frozen=True)
class GraphExtractionHooks:
    entity_extractor: Callable[[IRDocument], Iterable[IREntity]] | None = None
    relationship_extractor: Callable[[IRDocument], Iterable[IRProvenanceLink]] | None = None


FEATURE_FLAG_ENV = "CWI_GRAPH_RAG_ENTITY_EXTRACTION"


def build_graph_projection(
    documents: Iterable[IRDocument],
    tenant_id: str,
    graph_id: str,
    source: str,
    observed_utc: str,
    hooks: GraphExtractionHooks | None = None,
) -> GraphProjection:
    if not tenant_id or not graph_id or not source or not observed_utc:
        raise ValueError("tenant_id, graph_id, source, and observed_utc are required")

    nodes: List[Dict[str, object]] = []
    edges: List[Dict[str, object]] = []
    node_index: Dict[str, str] = {}
    node_payloads: Dict[str, Dict[str, object]] = {}
    edge_index: set[str] = set()

    enable_extraction = _feature_enabled(FEATURE_FLAG_ENV)

    def register_node(raw_id: str, node: Dict[str, object]) -> str:
        node_id = node["node_id"]
        node_index[raw_id] = node_id
        node_payloads[node_id] = node
        return node_id

    def ensure_external_node(raw_id: str) -> str:
        existing = node_index.get(raw_id)
        if existing:
            return existing
        node_id = f"external:{_stable_hash(raw_id)}"
        node = _node_payload(
            node_id=node_id,
            labels=["External"],
            properties={"reference_id": raw_id},
            tenant_id=tenant_id,
            graph_id=graph_id,
            source=source,
            observed_utc=observed_utc,
        )
        node_payloads[node_id] = node
        node_index[raw_id] = node_id
        return node_id

    for document in documents:
        entities = list(document.entities)
        provenance_links = list(document.provenance_links)
        if enable_extraction and hooks:
            if hooks.entity_extractor:
                entities.extend(list(hooks.entity_extractor(document)))
            if hooks.relationship_extractor:
                provenance_links.extend(list(hooks.relationship_extractor(document)))

        doc_node_id, doc_node = _document_node(document, tenant_id, graph_id, source, observed_utc)
        register_node(document.doc_id, doc_node)

        for entity in entities:
            entity_node_id, entity_node = _entity_node(entity, tenant_id, graph_id, source, observed_utc)
            register_node(entity.entity_id, entity_node)
            edge_id = _edge_id(doc_node_id, entity_node_id, "MENTIONS")
            if edge_id not in edge_index:
                edge_index.add(edge_id)
                edges.append(
                    _edge_payload(
                        edge_id=edge_id,
                        from_node_id=doc_node_id,
                        to_node_id=entity_node_id,
                        relation="MENTIONS",
                        properties={"doc_id": document.doc_id, "entity_type": entity.entity_type},
                        tenant_id=tenant_id,
                        graph_id=graph_id,
                        source=source,
                        observed_utc=observed_utc,
                    )
                )

        for link in provenance_links:
            from_id = node_index.get(link.from_id) or ensure_external_node(link.from_id)
            to_id = node_index.get(link.to_id) or ensure_external_node(link.to_id)
            edge_id = _edge_id(from_id, to_id, link.relation)
            if edge_id not in edge_index:
                edge_index.add(edge_id)
                edges.append(
                    _edge_payload(
                        edge_id=edge_id,
                        from_node_id=from_id,
                        to_node_id=to_id,
                        relation=link.relation,
                        properties={"source_id": link.source_id},
                        tenant_id=tenant_id,
                        graph_id=graph_id,
                        source=source,
                        observed_utc=observed_utc,
                    )
                )

    nodes = list(node_payloads.values())
    return GraphProjection(nodes=nodes, edges=edges)


def _feature_enabled(name: str) -> bool:
    value = os.environ.get(name, "").strip().lower()
    return value in {"1", "true", "yes", "on"}


def _document_node(
    document: IRDocument,
    tenant_id: str,
    graph_id: str,
    source: str,
    observed_utc: str,
) -> Tuple[str, Dict[str, object]]:
    node_id = f"doc:{document.source_id}:{document.doc_id}"
    properties: Dict[str, object] = {
        "doc_id": document.doc_id,
        "source_id": document.source_id,
        "entity_count": len(document.entities),
        "attribute_count": len(document.attributes),
    }
    properties.update(document.metadata or {})
    return node_id, _node_payload(
        node_id=node_id,
        labels=["Document"],
        properties=properties,
        tenant_id=tenant_id,
        graph_id=graph_id,
        source=source,
        observed_utc=observed_utc,
    )


def _entity_node(
    entity: IREntity,
    tenant_id: str,
    graph_id: str,
    source: str,
    observed_utc: str,
) -> Tuple[str, Dict[str, object]]:
    entity_id = entity.entity_id or _stable_hash(_stable_json(entity.attributes))
    node_id = f"entity:{entity.entity_type}:{entity_id}"
    properties = _attributes_to_properties(entity.attributes)
    properties.update({"entity_id": entity.entity_id, "entity_type": entity.entity_type})
    return node_id, _node_payload(
        node_id=node_id,
        labels=[entity.entity_type],
        properties=properties,
        tenant_id=tenant_id,
        graph_id=graph_id,
        source=source,
        observed_utc=observed_utc,
    )


def _attributes_to_properties(attributes: List[IRAttribute]) -> Dict[str, object]:
    properties: Dict[str, object] = {}
    for attribute in attributes:
        if attribute.name in properties:
            continue
        properties[attribute.name] = attribute.value
    return properties


def _node_payload(
    node_id: str,
    labels: List[str],
    properties: Dict[str, object],
    tenant_id: str,
    graph_id: str,
    source: str,
    observed_utc: str,
) -> Dict[str, object]:
    return {
        "node_id": node_id,
        "labels": labels,
        "properties": properties,
        "source": source,
        "tenant_id": tenant_id,
        "graph_id": graph_id,
        "created_utc": observed_utc,
        "updated_utc": observed_utc,
    }


def _edge_payload(
    edge_id: str,
    from_node_id: str,
    to_node_id: str,
    relation: str,
    properties: Dict[str, object],
    tenant_id: str,
    graph_id: str,
    source: str,
    observed_utc: str,
) -> Dict[str, object]:
    return {
        "edge_id": edge_id,
        "from_node_id": from_node_id,
        "to_node_id": to_node_id,
        "relation": relation,
        "directed": True,
        "properties": properties,
        "source": source,
        "tenant_id": tenant_id,
        "graph_id": graph_id,
        "created_utc": observed_utc,
        "updated_utc": observed_utc,
    }


def _edge_id(from_node_id: str, to_node_id: str, relation: str) -> str:
    return f"edge:{_stable_hash(f'{from_node_id}:{to_node_id}:{relation}')}"


def _stable_hash(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def _stable_json(attributes: List[IRAttribute]) -> str:
    payload = [
        {
            "name": attr.name,
            "value": attr.value,
            "value_type": attr.value_type,
            "source_id": attr.source_id,
            "confidence": attr.confidence,
            "metadata": attr.metadata,
        }
        for attr in attributes
    ]
    return json.dumps(payload, sort_keys=True, separators=(",", ":"), ensure_ascii=True)
