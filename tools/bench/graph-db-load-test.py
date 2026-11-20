import argparse
import os
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ETL_ROOT = ROOT / "src" / "etl"
if str(ETL_ROOT) not in sys.path:
    sys.path.insert(0, str(ETL_ROOT))

from cwi_etl.graph_rag import GraphProjection
from cwi_etl.graph_store import GraphIngestClient, GraphStoreConfig


def build_projection(node_count: int, edge_count: int, tenant_id: str, graph_id: str) -> GraphProjection:
    nodes = []
    edges = []
    observed = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")

    for idx in range(node_count):
        nodes.append(
            {
                "node_id": f"node-{idx}",
                "labels": ["LoadTest"],
                "tenant_id": tenant_id,
                "graph_id": graph_id,
                "source": "load-test",
                "created_utc": observed,
                "updated_utc": observed,
                "properties": {"ordinal": idx},
            }
        )

    for idx in range(edge_count):
        from_id = f"node-{idx % node_count}"
        to_id = f"node-{(idx + 1) % node_count}"
        edges.append(
            {
                "edge_id": f"edge-{idx}",
                "from_node_id": from_id,
                "to_node_id": to_id,
                "relation": "CONNECTED_TO",
                "tenant_id": tenant_id,
                "graph_id": graph_id,
                "source": "load-test",
                "created_utc": observed,
                "updated_utc": observed,
                "properties": {"ordinal": idx},
            }
        )

    return GraphProjection(nodes=nodes, edges=edges)


def main() -> int:
    parser = argparse.ArgumentParser(description="Graph DB ingestion load test")
    parser.add_argument("--dsn", default=None, help="Postgres DSN (defaults to CWI_GRAPH_DB_DSN)")
    parser.add_argument("--graph-name", default="cwi_graph", help="Graph name")
    parser.add_argument("--nodes", type=int, default=1000, help="Number of nodes")
    parser.add_argument("--edges", type=int, default=2000, help="Number of edges")
    parser.add_argument("--batch-size", type=int, default=100, help="Batch size")
    parser.add_argument("--tenant-id", default="tenant-1", help="Tenant id")
    parser.add_argument("--graph-id", default="graph-1", help="Graph id")
    parser.add_argument("--output", default=None, help="Optional output file")
    args = parser.parse_args()

    dsn = args.dsn or os.environ.get("CWI_GRAPH_DB_DSN", "")
    if not dsn:
        raise SystemExit("CWI_GRAPH_DB_DSN is required (or pass --dsn)")

    config = GraphStoreConfig(
        dsn=dsn,
        graph_name=args.graph_name,
        create_graph=True,
        batch_size=args.batch_size,
    )
    client = GraphIngestClient(config)

    projection = build_projection(args.nodes, args.edges, args.tenant_id, args.graph_id)

    start = time.perf_counter()
    client.ingest_projection(projection)
    elapsed = time.perf_counter() - start

    nodes_per_sec = args.nodes / elapsed if elapsed else 0.0
    edges_per_sec = args.edges / elapsed if elapsed else 0.0

    result = (
        f"nodes={args.nodes} edges={args.edges} elapsed_seconds={elapsed:.3f} "
        f"nodes_per_sec={nodes_per_sec:.2f} edges_per_sec={edges_per_sec:.2f}"
    )
    print(result)

    if args.output:
        output_path = Path(args.output)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(result + "\n", encoding="utf-8")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
