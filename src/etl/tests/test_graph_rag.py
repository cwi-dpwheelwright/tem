from cwi_etl.graph_rag import GraphExtractionHooks, build_graph_projection
from cwi_etl.ir import IRAttribute, IRDocument, IREntity, IRProvenanceLink


def test_graph_rag_projection_builds_nodes_and_edges():
    document = IRDocument(
        doc_id="doc-1",
        source_id="source-a",
        entities=[
            IREntity(
                entity_id="user-1",
                entity_type="Person",
                attributes=[
                    IRAttribute(
                        name="name",
                        value="Operator One",
                        value_type="string",
                        source_id="source-a",
                    )
                ],
                source_id="source-a",
            )
        ],
        provenance_links=[
            IRProvenanceLink(
                from_id="user-1",
                to_id="asset-1",
                relation="OWNS",
                source_id="source-a",
            )
        ],
        metadata={"title": "Sample Report"},
    )

    projection = build_graph_projection(
        [document],
        tenant_id="tenant-1",
        graph_id="graphrag",
        source="etl",
        observed_utc="2026-02-02T00:00:00Z",
    )

    assert projection.nodes
    assert projection.edges
    node_ids = {node["node_id"] for node in projection.nodes}
    edge_node_ids = {edge["from_node_id"] for edge in projection.edges} | {
        edge["to_node_id"] for edge in projection.edges
    }
    assert edge_node_ids.issubset(node_ids)

    external_nodes = [node for node in projection.nodes if "External" in node["labels"]]
    assert external_nodes

    for node in projection.nodes:
        assert node["created_utc"].endswith("Z")
        assert node["tenant_id"] == "tenant-1"
        assert node["graph_id"] == "graphrag"

    for edge in projection.edges:
        assert edge["created_utc"].endswith("Z")
        assert edge["directed"] is True


def test_graph_rag_projection_is_deterministic():
    document = IRDocument(
        doc_id="doc-1",
        source_id="source-a",
        entities=[],
        provenance_links=[],
    )

    projection_a = build_graph_projection(
        [document],
        tenant_id="tenant-1",
        graph_id="graphrag",
        source="etl",
        observed_utc="2026-02-02T00:00:00Z",
    )
    projection_b = build_graph_projection(
        [document],
        tenant_id="tenant-1",
        graph_id="graphrag",
        source="etl",
        observed_utc="2026-02-02T00:00:00Z",
    )

    assert projection_a.nodes == projection_b.nodes
    assert projection_a.edges == projection_b.edges


def test_graph_rag_extraction_hooks_disabled_by_default(monkeypatch):
    document = IRDocument(
        doc_id="doc-2",
        source_id="source-b",
        entities=[],
        provenance_links=[],
    )

    hooks = GraphExtractionHooks(
        entity_extractor=lambda doc: [
            IREntity(
                entity_id="extracted-1",
                entity_type="Person",
                attributes=[],
                source_id=doc.source_id,
            )
        ],
        relationship_extractor=lambda doc: [
            IRProvenanceLink(
                from_id="extracted-1",
                to_id=doc.doc_id,
                relation="RELATED_TO",
                source_id=doc.source_id,
            )
        ],
    )

    monkeypatch.delenv("CWI_GRAPH_RAG_ENTITY_EXTRACTION", raising=False)
    projection = build_graph_projection(
        [document],
        tenant_id="tenant-1",
        graph_id="graphrag",
        source="etl",
        observed_utc="2026-02-02T00:00:00Z",
        hooks=hooks,
    )

    assert len(projection.nodes) == 1
    assert len(projection.edges) == 0


def test_graph_rag_extraction_hooks_enabled(monkeypatch):
    document = IRDocument(
        doc_id="doc-3",
        source_id="source-c",
        entities=[],
        provenance_links=[],
    )

    hooks = GraphExtractionHooks(
        entity_extractor=lambda doc: [
            IREntity(
                entity_id="extracted-2",
                entity_type="Person",
                attributes=[
                    IRAttribute(
                        name="name",
                        value="Extracted Person",
                        value_type="string",
                        source_id=doc.source_id,
                    )
                ],
                source_id=doc.source_id,
            )
        ],
        relationship_extractor=lambda doc: [
            IRProvenanceLink(
                from_id="extracted-2",
                to_id=doc.doc_id,
                relation="RELATED_TO",
                source_id=doc.source_id,
            )
        ],
    )

    monkeypatch.setenv("CWI_GRAPH_RAG_ENTITY_EXTRACTION", "true")
    projection = build_graph_projection(
        [document],
        tenant_id="tenant-1",
        graph_id="graphrag",
        source="etl",
        observed_utc="2026-02-02T00:00:00Z",
        hooks=hooks,
    )

    labels = {tuple(node["labels"]) for node in projection.nodes}
    assert ("Person",) in labels
    assert projection.edges
