import os

import pytest

from cwi_etl.graph_rag import GraphProjection
from cwi_etl.graph_store import GraphIngestClient, GraphStoreConfig


def _dollar_quote(value: str, tag: str) -> str:
    token = f"cwi_{tag}"
    while f"${token}$" in value:
        token += "x"
    return f"${token}$" + value + f"${token}$"


@pytest.mark.integration
def test_graph_ingest_client_persists_nodes_and_edges():
    psycopg = pytest.importorskip("psycopg")
    testcontainers = pytest.importorskip("testcontainers.postgres")
    PostgresContainer = testcontainers.PostgresContainer

    image = os.environ.get("CWI_GRAPH_DB_IMAGE", "apache/age:latest")
    with PostgresContainer(image) as postgres:
        dsn = postgres.get_connection_url()
        if "+psycopg2" in dsn:
            dsn = dsn.replace("postgresql+psycopg2", "postgresql", 1)

        graph_name = "test_graph"
        config = GraphStoreConfig(dsn=dsn, graph_name=graph_name, create_graph=True, batch_size=10)
        client = GraphIngestClient(config)
        projection = GraphProjection(
            nodes=[
                {
                    "node_id": "node-1",
                    "labels": ["Document"],
                    "tenant_id": "tenant-1",
                    "graph_id": "graph-1",
                    "source": "test",
                    "created_utc": "2026-02-02T00:00:00Z",
                    "updated_utc": "2026-02-02T00:00:00Z",
                    "properties": {"title": "Doc 1"},
                },
                {
                    "node_id": "node-2",
                    "labels": ["Entity"],
                    "tenant_id": "tenant-1",
                    "graph_id": "graph-1",
                    "source": "test",
                    "created_utc": "2026-02-02T00:00:00Z",
                    "updated_utc": "2026-02-02T00:00:00Z",
                    "properties": {"name": "Entity 2"},
                },
            ],
            edges=[
                {
                    "edge_id": "edge-1",
                    "from_node_id": "node-1",
                    "to_node_id": "node-2",
                    "relation": "RELATED_TO",
                    "tenant_id": "tenant-1",
                    "graph_id": "graph-1",
                    "source": "test",
                    "created_utc": "2026-02-02T00:00:00Z",
                    "updated_utc": "2026-02-02T00:00:00Z",
                    "properties": {"weight": 1},
                }
            ],
        )

        client.ingest_projection(projection)

        with psycopg.connect(dsn) as conn:
            with conn.cursor() as cur:
                from psycopg import sql

                cur.execute("CREATE EXTENSION IF NOT EXISTS age;")
                cur.execute("LOAD 'age';")
                cur.execute('SET search_path = ag_catalog, "$user", public;')

                node_query = sql.SQL(
                    "SELECT * FROM ag_catalog.cypher({graph}, {query}) as (result agtype);"
                ).format(
                    graph=sql.Literal(graph_name),
                    query=sql.SQL(_dollar_quote("MATCH (n {node_id: 'node-1'}) RETURN n", "verify_query")),
                )
                cur.execute(node_query)
                assert cur.fetchone() is not None

                edge_query = sql.SQL(
                    "SELECT * FROM ag_catalog.cypher({graph}, {query}) as (result agtype);"
                ).format(
                    graph=sql.Literal(graph_name),
                    query=sql.SQL(
                        _dollar_quote(
                            "MATCH ()-[r:RELATED_TO {edge_id: 'edge-1'}]->() RETURN r",
                            "verify_query",
                        )
                    ),
                )
                cur.execute(edge_query)
                assert cur.fetchone() is not None
