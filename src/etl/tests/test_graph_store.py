import pytest

from cwi_etl.graph_store import build_edge_cypher, build_node_cypher


def test_build_node_cypher_sanitizes_labels_and_props():
    query = build_node_cypher(
        {
            "node_id": "node-1",
            "labels": ["Primary Label", "ops"],
            "properties": {"title": "Report"},
            "tenant_id": "tenant-1",
        }
    )

    assert "MERGE (n:Primary_Label:ops {node_id: 'node-1'})" in query
    assert "tenant_id: 'tenant-1'" in query
    assert "properties: {title: 'Report'}" in query


def test_build_edge_cypher_sanitizes_relation():
    query = build_edge_cypher(
        {
            "edge_id": "edge-1",
            "from_node_id": "node-a",
            "to_node_id": "node-b",
            "relation": "OWNS/USES",
            "properties": {"weight": 1},
        }
    )

    assert "MERGE (from)-[r:OWNS_USES" in query
    assert "edge_id: 'edge-1'" in query
    assert "properties: {weight: 1}" in query


def test_build_edge_cypher_defaults_relation():
    query = build_edge_cypher(
        {
            "edge_id": "edge-2",
            "from_node_id": "node-a",
            "to_node_id": "node-b",
        }
    )

    assert "MERGE (from)-[r:RELATED_TO" in query
