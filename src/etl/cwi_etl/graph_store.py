from __future__ import annotations

import os
import re
from dataclasses import dataclass
from typing import Dict, Iterable

from .graph_rag import GraphProjection

try:
    import psycopg
except ImportError:  # pragma: no cover - optional dependency
    psycopg = None

DEFAULT_GRAPH_NAME = "cwi_graph"


@dataclass(frozen=True)
class GraphStoreConfig:
    dsn: str
    graph_name: str = DEFAULT_GRAPH_NAME
    create_graph: bool = True
    batch_size: int = 100

    @staticmethod
    def from_env(prefix: str = "CWI_GRAPH_DB") -> "GraphStoreConfig":
        dsn = os.environ.get(f"{prefix}_DSN", "").strip()
        if not dsn:
            raise ValueError(f"{prefix}_DSN is required")
        graph_name = os.environ.get(f"{prefix}_NAME", DEFAULT_GRAPH_NAME).strip() or DEFAULT_GRAPH_NAME
        _validate_graph_name(graph_name)
        create_graph = _bool_env(os.environ.get(f"{prefix}_CREATE_GRAPH", "true"))
        batch_size = int(os.environ.get(f"{prefix}_BATCH_SIZE", "100"))
        return GraphStoreConfig(
            dsn=dsn,
            graph_name=graph_name,
            create_graph=create_graph,
            batch_size=batch_size,
        )


class GraphIngestClient:
    def __init__(self, config: GraphStoreConfig) -> None:
        if psycopg is None:
            raise RuntimeError("psycopg is required for graph ingestion")
        self.config = config

    def ingest_projection(self, projection: GraphProjection) -> None:
        if not projection.nodes and not projection.edges:
            return
        with psycopg.connect(self.config.dsn) as conn:
            with conn.cursor() as cur:
                _prepare_age(cur)
                if self.config.create_graph and not _graph_exists(cur, self.config.graph_name):
                    cur.execute("SELECT ag_catalog.create_graph(%s);", (self.config.graph_name,))

                _ingest_nodes(cur, self.config.graph_name, projection.nodes, self.config.batch_size)
                _ingest_edges(cur, self.config.graph_name, projection.edges, self.config.batch_size)
            conn.commit()


def build_node_cypher(node: Dict[str, object]) -> str:
    labels = node.get("labels") or ["Node"]
    label_fragment = ":" + ":".join(_sanitize_label(label) for label in labels if str(label).strip())
    props = _node_properties(node)
    node_id_literal = _cypher_literal(node["node_id"])
    props_literal = _cypher_literal(props)
    return f"MERGE (n{label_fragment} {{node_id: {node_id_literal}}}) SET n += {props_literal} RETURN n"


def build_edge_cypher(edge: Dict[str, object]) -> str:
    relation = _sanitize_relation(str(edge.get("relation") or "RELATED_TO"))
    props = _edge_properties(edge)
    from_literal = _cypher_literal(edge["from_node_id"])
    to_literal = _cypher_literal(edge["to_node_id"])
    edge_literal = _cypher_literal(edge["edge_id"])
    props_literal = _cypher_literal(props)
    return (
        f"MERGE (from {{node_id: {from_literal}}}) "
        f"MERGE (to {{node_id: {to_literal}}}) "
        f"MERGE (from)-[r:{relation} {{edge_id: {edge_literal}}}]->(to) "
        f"SET r += {props_literal} RETURN r"
    )


def _ingest_nodes(cur, graph_name: str, nodes: Iterable[Dict[str, object]], batch_size: int) -> None:
    for node in nodes:
        query = build_node_cypher(node)
        _execute_cypher(cur, graph_name, query)


def _ingest_edges(cur, graph_name: str, edges: Iterable[Dict[str, object]], batch_size: int) -> None:
    for edge in edges:
        query = build_edge_cypher(edge)
        _execute_cypher(cur, graph_name, query)


def _execute_cypher(cur, graph_name: str, query: str) -> None:
    from psycopg import sql

    statement = sql.SQL(
        "SELECT * FROM ag_catalog.cypher({graph}, {query}) as (result agtype);"
    ).format(
        graph=sql.Literal(graph_name),
        query=sql.SQL(_dollar_quote(query, "cypher_query")),
    )
    cur.execute(statement)  # nosemgrep: python.sqlalchemy.security.sqlalchemy-execute-raw-query


def _prepare_age(cur) -> None:
    cur.execute("CREATE EXTENSION IF NOT EXISTS age;")
    cur.execute("LOAD 'age';")
    cur.execute('SET search_path = ag_catalog, "$user", public;')


def _graph_exists(cur, graph_name: str) -> bool:
    cur.execute("SELECT 1 FROM ag_catalog.ag_graph WHERE name = %s;", (graph_name,))
    return cur.fetchone() is not None


def _node_properties(node: Dict[str, object]) -> Dict[str, object]:
    return {key: value for key, value in node.items() if key != "labels" and value is not None}


def _edge_properties(edge: Dict[str, object]) -> Dict[str, object]:
    return {key: value for key, value in edge.items() if key != "relation" and value is not None}


def _sanitize_label(label: object) -> str:
    value = str(label).strip()
    if not value:
        return "Node"
    return re.sub(r"[^A-Za-z0-9_]", "_", value)


def _sanitize_relation(relation: str) -> str:
    cleaned = re.sub(r"[^A-Za-z0-9_]", "_", relation.strip())
    return cleaned or "RELATED_TO"


def _validate_graph_name(name: str) -> None:
    if not re.match(r"^[A-Za-z_][A-Za-z0-9_]*$", name):
        raise ValueError("graph_name must be alphanumeric/underscore")


def _bool_env(value: str) -> bool:
    return value.strip().lower() in {"1", "true", "yes", "on"}


def _dollar_quote(value: str, tag: str) -> str:
    token = f"cwi_{tag}"
    while f"${token}$" in value:
        token += "x"
    return f"${token}$" + value + f"${token}$"


def _cypher_literal(value: object) -> str:
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, (int, float)):
        return str(value)
    if isinstance(value, dict):
        return _cypher_map_literal(value)
    if isinstance(value, (list, tuple)):
        return "[" + ", ".join(_cypher_literal(item) for item in value) + "]"
    return f"'{_escape_cypher_string(str(value))}'"


def _cypher_map_literal(values: Dict[str, object]) -> str:
    if not values:
        return "{}"
    items = []
    for key in sorted(values.keys(), key=lambda item: str(item)):
        cypher_key = _cypher_key(str(key))
        items.append(f"{cypher_key}: {_cypher_literal(values[key])}")
    return "{" + ", ".join(items) + "}"


def _cypher_key(key: str) -> str:
    if re.match(r"^[A-Za-z_][A-Za-z0-9_]*$", key):
        return key
    return f"`{key.replace('`', '``')}`"


def _escape_cypher_string(value: str) -> str:
    return value.replace("\\", "\\\\").replace("'", "\\'")
